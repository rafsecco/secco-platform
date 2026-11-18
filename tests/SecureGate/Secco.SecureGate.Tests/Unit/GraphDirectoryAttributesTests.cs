using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Infrastructure.Federation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Leitura de cargo, departamento, telefone e gestor no Graph (ADR-0055). Prova o FORMATO da chamada e a postura
/// fail-closed contra um servidor falso; os valores saem CRUS — normalizar e validar é da regra de atributos.
/// </summary>
public class GraphDirectoryAttributesTests
{
	private static readonly Guid DirectoryId = Guid.Parse("018f0000-0000-7000-8000-0000000000cc");
	private static readonly Guid GroupId = Guid.Parse("018f0000-0000-7000-8000-0000000000aa");
	private const string GraphBase = "https://graph.microsoft.com/v1.0/";
	private const string TokenBody = "{\"access_token\":\"tok-1\",\"expires_in\":3600}";
	private const string AttributesSelect = "id,jobTitle,department,businessPhones";

	private static GraphGroupDirectory Build(ScriptedHandler handler)
	{
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(GraphGroupDirectory.HttpClientName).Returns(new HttpClient(handler));

		return new GraphGroupDirectory(
			httpClientFactory,
			Options.Create(new SecureGateEntraIdOptions { ClientId = "client-id", ClientSecret = "client-secret" }),
			new GraphAppTokenCache(),
			NullLogger<GraphGroupDirectory>.Instance);
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
	{
		Content = new StringContent(body, Encoding.UTF8, "application/json"),
	};

	private static bool IsTokenRequest(HttpRequestMessage request) =>
		request.RequestUri!.Host.Contains("login.microsoftonline.com", StringComparison.Ordinal);

	private static bool IsManagerRequest(HttpRequestMessage request) =>
		request.RequestUri!.AbsolutePath.EndsWith("/manager", StringComparison.Ordinal);

	private static string Member(Guid id, string? extra = null) =>
		$"{{\"@odata.type\":\"#microsoft.graph.user\",\"id\":\"{id:D}\"{(extra is null ? string.Empty : "," + extra)}}}";

	private static string Page(string[] members, string? next = null) =>
		$"{{\"value\":[{string.Join(",", members)}]{(next is null ? string.Empty : $",\"@odata.nextLink\":\"{next}\"")}}}";

	/// <summary>Programa token, membros e gestor (404 quando o gestor do oid não foi informado).</summary>
	private static ScriptedHandler Script(string membersBody, Dictionary<Guid, string>? managers = null) =>
		new(r =>
		{
			if (IsTokenRequest(r))
			{
				return Json(HttpStatusCode.OK, TokenBody);
			}

			if (IsManagerRequest(r))
			{
				var oid = Guid.Parse(r.RequestUri!.AbsolutePath.Split('/')[^2]);

				return managers is not null && managers.TryGetValue(oid, out var body)
					? Json(HttpStatusCode.OK, body)
					: Json(HttpStatusCode.NotFound, "{\"error\":{}}");
			}

			return Json(HttpStatusCode.OK, membersBody);
		});

	[Fact]
	public async Task Atributos_MontaAUrlComSelectFixo()
	{
		var handler = Script(Page([]));

		await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		handler.Requests.Single(r => !IsTokenRequest(r)).RequestUri!.ToString().Should().Be(
			$"{GraphBase}groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$select={AttributesSelect}&$top=999");
	}

	[Fact]
	public async Task Atributos_LeGestorPorPessoa()
	{
		var ana = Guid.NewGuid();
		var bia = Guid.NewGuid();
		var chefe = Guid.NewGuid();
		var handler = Script(
			Page([Member(ana, "\"jobTitle\":\"Dev\",\"department\":\"TI\",\"businessPhones\":[\"+55 11 1111\"]"), Member(bia)]),
			new Dictionary<Guid, string> { [ana] = $"{{\"id\":\"{chefe:D}\"}}", [bia] = $"{{\"id\":\"{chefe:D}\"}}" });

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEquivalentTo(
		[
			new DirectoryAttributes(ana, "Dev", "TI", "+55 11 1111", chefe),
			new DirectoryAttributes(bia, null, null, null, chefe),
		]);
		handler.Requests.Where(IsManagerRequest).Select(r => r.RequestUri!.ToString()).Should().BeEquivalentTo(
		[
			$"{GraphBase}users/{ana:D}/manager?$select=id",
			$"{GraphBase}users/{bia:D}/manager?$select=id",
		]);
	}

	[Fact]
	public async Task Atributos_GestorAusente404_ViraNulo()
	{
		var ana = Guid.NewGuid();
		var handler = Script(Page([Member(ana)]));

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().ContainSingle().Which.ManagerObjectId.Should().BeNull();
	}

	[Fact]
	public async Task Atributos_UmGestorFalha_LeituraInteiraFalha()
	{
		var ana = Guid.NewGuid();
		var bia = Guid.NewGuid();
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody)
			: IsManagerRequest(r) ? Json(HttpStatusCode.TooManyRequests, "{}")
			: Json(HttpStatusCode.OK, Page([Member(ana), Member(bia)])));

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryUnavailable");
	}

	[Fact]
	public async Task Atributos_GestorComOutraFalha_ConsentRequiredDerrubaALeitura()
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody)
			: IsManagerRequest(r) ? Json(HttpStatusCode.Forbidden, "{}")
			: Json(HttpStatusCode.OK, Page([Member(Guid.NewGuid())])));

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.ConsentRequired");
	}

	[Fact]
	public async Task Atributos_Sem403Consentimento_ConsentRequired()
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.Forbidden, "{}"));

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.Error.Code.Should().Be("SecureGate.Federation.ConsentRequired");
		handler.Requests.Should().NotContain(r => IsManagerRequest(r));
	}

	[Fact]
	public async Task Atributos_PaginacaoENextLinkForaDoGraph()
	{
		var ana = Guid.NewGuid();
		var bia = Guid.NewGuid();
		var next = $"{GraphBase}groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$skiptoken=abc";
		var paged = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody)
			: IsManagerRequest(r) ? Json(HttpStatusCode.NotFound, "{}")
			: r.RequestUri!.ToString() == next ? Json(HttpStatusCode.OK, Page([Member(bia)]))
			: Json(HttpStatusCode.OK, Page([Member(ana)], next)));

		var result = await Build(paged).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsSuccess.Should().BeTrue();
		result.Value.Select(a => a.ObjectId).Should().BeEquivalentTo([ana, bia]);
		paged.Requests.Count(r => !IsTokenRequest(r) && !IsManagerRequest(r)).Should().Be(2);

		var foreign = Script(Page([Member(ana)], "https://attacker.example/steal"));

		var rejected = await Build(foreign).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		rejected.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
		foreign.Requests.Should().NotContain(r => r.RequestUri!.Host == "attacker.example");
	}

	[Theory]
	[InlineData("\"businessPhones\":[]", null)]
	[InlineData("\"businessPhones\":null", null)]
	[InlineData("\"jobTitle\":\"x\"", null)]
	[InlineData("\"businessPhones\":[\"\"]", "")]
	[InlineData("\"businessPhones\":[\"+55 11 1111\",\"+55 11 2222\"]", "+55 11 1111")]
	public async Task Atributos_Telefone_PrimeiroElementoCruOuNulo(string extra, string? expected)
	{
		var handler = Script(Page([Member(Guid.NewGuid(), extra)]));

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().ContainSingle().Which.BusinessPhone.Should().Be(expected);
	}

	[Theory]
	[InlineData("{\"value\":[{\"id\":\"018f0000-0000-7000-8000-0000000000bb\",\"jobTitle\":1}]}")]
	[InlineData("{\"value\":[{\"id\":\"018f0000-0000-7000-8000-0000000000bb\",\"department\":{\"x\":1}}]}")]
	[InlineData("{\"value\":[{\"id\":\"018f0000-0000-7000-8000-0000000000bb\",\"businessPhones\":\"x\"}]}")]
	[InlineData("{\"value\":[{\"id\":\"018f0000-0000-7000-8000-0000000000bb\",\"businessPhones\":[1]}]}")]
	[InlineData("{\"value\":[{\"jobTitle\":\"Dev\"}]}")]
	[InlineData("{}")]
	public async Task Atributos_RespostaMalformada_LeituraIncompleta(string body)
	{
		var handler = Script(body);

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
	}

	[Fact]
	public async Task Atributos_GestorComIdNaoGuid_LeituraIncompleta()
	{
		var ana = Guid.NewGuid();
		var handler = Script(Page([Member(ana)]), new Dictionary<Guid, string> { [ana] = "{\"id\":\"nao-e-guid\"}" });

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
	}

	[Fact]
	public async Task Atributos_GestorComIdDeContatoQualquer_DevolveOOid()
	{
		var ana = Guid.NewGuid();
		var contato = Guid.NewGuid();
		var handler = Script(
			Page([Member(ana)]),
			new Dictionary<Guid, string>
			{
				[ana] = $"{{\"@odata.type\":\"#microsoft.graph.orgContact\",\"id\":\"{contato:D}\"}}",
			});

		var result = await Build(handler).GetGroupMemberAttributesAsync(DirectoryId, GroupId);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().ContainSingle().Which.ManagerObjectId.Should().Be(contato);
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
