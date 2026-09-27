using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Secco.SecureGate.Infrastructure.Federation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Cliente HTTP puro contra o Microsoft Graph (issue #27, ADR-0036) — prova o FORMATO da chamada
/// (token, URL, mapeamento de erro) contra um servidor falso. Não prova o comportamento real do
/// Graph nem de consentimento — isso exige um tenant Entra de teste (registrado na ADR-0036).
/// </summary>
public class GraphGroupDirectoryTests
{
	private static readonly Guid DirectoryId = Guid.Parse("018f0000-0000-7000-8000-0000000000cc");
	private const string GraphGroupsBase = "https://graph.microsoft.com/v1.0/groups";

	private static GraphGroupDirectory Build(ScriptedHandler handler, bool configured = true)
	{
		// Sem 'using': o client precisa sobreviver ao retorno deste método — quem chama descarta.
		var client = new HttpClient(handler);
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(GraphGroupDirectory.HttpClientName).Returns(client);

		var options = Options.Create(new SecureGateEntraIdOptions
		{
			ClientId = configured ? "client-id" : null,
			ClientSecret = configured ? "client-secret" : null,
		});

		return new GraphGroupDirectory(
			httpClientFactory, options, new GraphAppTokenCache(), NullLogger<GraphGroupDirectory>.Instance);
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
	{
		Content = new StringContent(body, Encoding.UTF8, "application/json"),
	};

	private static readonly string TokenSuccessBody = "{\"access_token\":\"tok-1\",\"expires_in\":3600}";

	[Fact]
	public async Task ListGroupsAsync_AppNaoConfigurada_RetornaNotEnabledSemChamarHttp()
	{
		var handler = new ScriptedHandler(_ => throw new InvalidOperationException("não deveria chamar o Graph"));
		var directory = Build(handler, configured: false);

		var result = await directory.ListGroupsAsync(DirectoryId, null, null, 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.NotEnabled");
		handler.Requests.Should().BeEmpty();
	}

	[Fact]
	public async Task ListGroupsAsync_PageTokenForaDoDominioDoGraph_RecusaSemChamarHttp()
	{
		var handler = new ScriptedHandler(_ => throw new InvalidOperationException("não deveria chamar o Graph"));
		var directory = Build(handler);

		// Defesa contra SSRF (ADR-0020): o token de aplicação da plataforma não pode ser usado
		// para requisitar uma URL arbitrária vinda do chamador.
		var result = await directory.ListGroupsAsync(DirectoryId, null, "https://attacker.example/steal", 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.InvalidPageToken");
		handler.Requests.Should().BeEmpty();
	}

	[Fact]
	public async Task ListGroupsAsync_ComSucesso_DevolveOsGruposEOTokenDaProximaPagina()
	{
		var handler = new ScriptedHandler(request => request.RequestUri!.Host.Contains("login.microsoftonline.com")
			? Json(HttpStatusCode.OK, TokenSuccessBody)
			: Json(HttpStatusCode.OK,
				"""{"value":[{"id":"g1","displayName":"Financeiro"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/groups?$skiptoken=abc"}"""));

		var result = await Build(handler).ListGroupsAsync(DirectoryId, null, null, 20);

		result.IsSuccess.Should().BeTrue();
		result.Value.Items.Should().ContainSingle(g => g.Id == "g1" && g.DisplayName == "Financeiro");
		result.Value.NextPageToken.Should().Be("https://graph.microsoft.com/v1.0/groups?$skiptoken=abc");
	}

	[Fact]
	public async Task ListGroupsAsync_UltimaPagina_NaoDevolveTokenDeProximaPagina()
	{
		var handler = new ScriptedHandler(request => request.RequestUri!.Host.Contains("login.microsoftonline.com")
			? Json(HttpStatusCode.OK, TokenSuccessBody)
			: Json(HttpStatusCode.OK, """{"value":[]}"""));

		var result = await Build(handler).ListGroupsAsync(DirectoryId, null, null, 20);

		result.IsSuccess.Should().BeTrue();
		result.Value.Items.Should().BeEmpty();
		result.Value.NextPageToken.Should().BeNull();
	}

	[Fact]
	public async Task ListGroupsAsync_ComFiltro_MontaOFilterOData()
	{
		var handler = new ScriptedHandler(request => request.RequestUri!.Host.Contains("login.microsoftonline.com")
			? Json(HttpStatusCode.OK, TokenSuccessBody)
			: Json(HttpStatusCode.OK, """{"value":[]}"""));

		await Build(handler).ListGroupsAsync(DirectoryId, "Financ''eiro", null, 20);

		var groupsRequest = handler.Requests.Single(r => r.RequestUri!.ToString().StartsWith(GraphGroupsBase, StringComparison.Ordinal));
		groupsRequest.RequestUri!.ToString().Should()
			.Contain(Uri.EscapeDataString("startswith(displayName,'Financ''eiro')"));
	}

	[Fact]
	public async Task ListGroupsAsync_TokenRecusadoComQuatroZeroZero_RetornaConsentRequired()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_client"}"""));

		var result = await Build(handler).ListGroupsAsync(DirectoryId, null, null, 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.ConsentRequired");
	}

	[Fact]
	public async Task ListGroupsAsync_TokenIndisponivel_RetornaDirectoryUnavailable()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.InternalServerError, "erro interno"));

		var result = await Build(handler).ListGroupsAsync(DirectoryId, null, null, 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryUnavailable");
	}

	[Fact]
	public async Task ListGroupsAsync_GraphRecusaComForbidden_RetornaConsentRequired()
	{
		var handler = new ScriptedHandler(request => request.RequestUri!.Host.Contains("login.microsoftonline.com")
			? Json(HttpStatusCode.OK, TokenSuccessBody)
			: Json(HttpStatusCode.Forbidden, """{"error":{"code":"Authorization_RequestDenied"}}"""));

		var result = await Build(handler).ListGroupsAsync(DirectoryId, null, null, 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.ConsentRequired");
	}

	[Fact]
	public async Task ListGroupsAsync_GraphIndisponivel_RetornaDirectoryUnavailable()
	{
		var handler = new ScriptedHandler(request => request.RequestUri!.Host.Contains("login.microsoftonline.com")
			? Json(HttpStatusCode.OK, TokenSuccessBody)
			: Json(HttpStatusCode.ServiceUnavailable, "indisponível"));

		var result = await Build(handler).ListGroupsAsync(DirectoryId, null, null, 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryUnavailable");
	}

	[Fact]
	public async Task ListGroupsAsync_ComPageTokenDoGraph_ChamaAUrlDiretoSemMontarFiltro()
	{
		var handler = new ScriptedHandler(request => request.RequestUri!.Host.Contains("login.microsoftonline.com")
			? Json(HttpStatusCode.OK, TokenSuccessBody)
			: Json(HttpStatusCode.OK, """{"value":[{"id":"g2","displayName":"RH"}]}"""));

		var pageToken = "https://graph.microsoft.com/v1.0/groups?$skiptoken=abc";
		var result = await Build(handler).ListGroupsAsync(DirectoryId, null, pageToken, 20);

		result.IsSuccess.Should().BeTrue();
		result.Value.Items.Should().ContainSingle(g => g.Id == "g2");
		handler.Requests.Should().Contain(r => r.RequestUri!.ToString() == pageToken);
	}

	[Fact]
	public async Task ListGroupsAsync_DuasChamadasParaOMesmoDiretorio_ReusaOTokenEmCache()
	{
		var tokenCalls = 0;
		var handler = new ScriptedHandler(request =>
		{
			if (request.RequestUri!.Host.Contains("login.microsoftonline.com"))
			{
				tokenCalls++;

				return Json(HttpStatusCode.OK, TokenSuccessBody);
			}

			return Json(HttpStatusCode.OK, """{"value":[]}""");
		});

		var directory = Build(handler);

		await directory.ListGroupsAsync(DirectoryId, null, null, 20);
		await directory.ListGroupsAsync(DirectoryId, null, null, 20);

		tokenCalls.Should().Be(1, "o token de aplicação vale por até 1h — pedir de novo a cada listagem seria desperdício");
	}

	private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public List<HttpRequestMessage> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request);

			return Task.FromResult(respond(request));
		}
	}
}
