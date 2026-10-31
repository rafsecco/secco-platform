using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Infrastructure.Federation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// As duas leituras do Graph da reconciliação (ADR-0055): membros transitivos de um grupo e pertinência de um
/// usuário a vários grupos. Prova o FORMATO da chamada e a postura fail-closed contra um servidor falso — não o
/// comportamento real do Graph nem do consentimento, que exige um tenant Entra de teste (ADR-0036/0055).
/// </summary>
public class GraphDirectoryMembershipTests
{
	private static readonly Guid DirectoryId = Guid.Parse("018f0000-0000-7000-8000-0000000000cc");
	private static readonly Guid GroupId = Guid.Parse("018f0000-0000-7000-8000-0000000000aa");
	private static readonly Guid UserOid = Guid.Parse("018f0000-0000-7000-8000-0000000000bb");
	private const string GraphBase = "https://graph.microsoft.com/v1.0/";
	private const string TokenBody = "{\"access_token\":\"tok-1\",\"expires_in\":3600}";

	private static GraphGroupDirectory Build(ScriptedHandler handler, bool configured = true)
	{
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(GraphGroupDirectory.HttpClientName).Returns(new HttpClient(handler));

		return new GraphGroupDirectory(
			httpClientFactory,
			Options.Create(new SecureGateEntraIdOptions
			{
				ClientId = configured ? "client-id" : null,
				ClientSecret = configured ? "client-secret" : null,
			}),
			new GraphAppTokenCache(),
			NullLogger<GraphGroupDirectory>.Instance);
	}

	[Fact]
	public async Task Pertinencia_GraphQueNaoResponde_EstouraOLimiteDoLoginEFalhaSemPendurar()
	{
		var handler = new HangingHandler(TokenBody);
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(GraphGroupDirectory.HttpClientName).Returns(new HttpClient(handler));
		var directory = new GraphGroupDirectory(
			httpClientFactory,
			Options.Create(new SecureGateEntraIdOptions { ClientId = "c", ClientSecret = "s" }),
			new GraphAppTokenCache(),
			NullLogger<GraphGroupDirectory>.Instance)
		{
			CheckMemberGroupsTimeout = TimeSpan.FromMilliseconds(200),
		};

		var result = await directory.CheckMemberGroupsAsync(DirectoryId, UserOid, [Guid.NewGuid()]);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryUnavailable");
	}

	[Fact]
	public async Task Pertinencia_CancelamentoDoChamador_PropagaECancelamento()
	{
		var handler = new HangingHandler(TokenBody);
		var httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(GraphGroupDirectory.HttpClientName).Returns(new HttpClient(handler));
		var directory = new GraphGroupDirectory(
			httpClientFactory,
			Options.Create(new SecureGateEntraIdOptions { ClientId = "c", ClientSecret = "s" }),
			new GraphAppTokenCache(),
			NullLogger<GraphGroupDirectory>.Instance);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		var act = () => directory.CheckMemberGroupsAsync(DirectoryId, UserOid, [Guid.NewGuid()], cts.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
	{
		Content = new StringContent(body, Encoding.UTF8, "application/json"),
	};

	private static bool IsTokenRequest(HttpRequestMessage request) =>
		request.RequestUri!.Host.Contains("login.microsoftonline.com", StringComparison.Ordinal);

	private static string Page(IEnumerable<Guid> ids, string? next = null)
	{
		var value = string.Join(",", ids.Select(id => $"{{\"@odata.type\":\"#microsoft.graph.user\",\"id\":\"{id:D}\"}}"));
		var link = next is null ? string.Empty : $",\"@odata.nextLink\":\"{next}\"";

		return $"{{\"value\":[{value}]{link}}}";
	}

	// ---- Membros transitivos do grupo ----

	[Fact]
	public async Task Membros_MontaAUrlComGuidESoUsuarios_ComTop999()
	{
		var handler = new ScriptedHandler(r => IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, Page([])));

		await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		handler.Requests.Single(r => !IsTokenRequest(r)).RequestUri!.ToString().Should().Be(
			$"{GraphBase}groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$select=id&$top=999");
		handler.Requests.Single(r => !IsTokenRequest(r)).Headers.Authorization!.ToString().Should().Be("Bearer tok-1");
	}

	[Fact]
	public async Task Membros_PercorreAPaginacaoAteOFim_ESomaTudoSemRepeticao()
	{
		var first = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
		var second = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToList();
		var next = $"{GraphBase}groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$skiptoken=abc";
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody)
			: r.RequestUri!.ToString() == next ? Json(HttpStatusCode.OK, Page([.. second, first[0]]))
			: Json(HttpStatusCode.OK, Page(first, next)));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEquivalentTo([.. first, .. second]);
		handler.Requests.Count(r => !IsTokenRequest(r)).Should().Be(2);
	}

	[Fact]
	public async Task Membros_SegundaPaginaFalha_FalhaSemDevolverOQueJaLeu()
	{
		var next = $"{GraphBase}groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$skiptoken=abc";
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody)
			: r.RequestUri!.ToString() == next ? Json(HttpStatusCode.ServiceUnavailable, "indisponível")
			: Json(HttpStatusCode.OK, Page([Guid.NewGuid()], next)));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue("uma lista truncada retiraria perfis de quem ficou fora dela");
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryUnavailable");
	}

	[Fact]
	public async Task Membros_NextLinkForaDoGraph_FalhaSemSeguirAUrl()
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody)
			: Json(HttpStatusCode.OK, Page([Guid.NewGuid()], "https://attacker.example/steal")));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue("o token de aplicação não segue para onde o cursor mandar (SSRF)");
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
		handler.Requests.Should().NotContain(r => r.RequestUri!.Host == "attacker.example");
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("{\"value\":{}}")]
	[InlineData("{\"value\":[{\"id\":\"nao-e-guid\"}]}")]
	[InlineData("{\"value\":[{\"semId\":1}]}")]
	[InlineData("[]")]
	public async Task Membros_RespostaMalformada_FalhaComoLeituraIncompleta(string body)
	{
		var handler = new ScriptedHandler(r => IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, body));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
	}

	[Fact]
	public async Task Membros_CorpoNaoJson_FalhaComoIndisponivel()
	{
		var handler = new ScriptedHandler(r => IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, "<html>"));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryUnavailable");
	}

	[Fact]
	public async Task Membros_PaginacaoQueNuncaFecha_FalhaNoTeto()
	{
		var next = $"{GraphBase}groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$skiptoken=loop";
		var handler = new ScriptedHandler(r => IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, Page([], next)));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
		handler.Requests.Count(r => !IsTokenRequest(r)).Should().Be(1000);
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden, "SecureGate.Federation.ConsentRequired")]
	[InlineData(HttpStatusCode.Unauthorized, "SecureGate.Federation.ConsentRequired")]
	[InlineData(HttpStatusCode.NotFound, "SecureGate.Federation.DirectoryObjectNotFound")]
	[InlineData(HttpStatusCode.TooManyRequests, "SecureGate.Federation.DirectoryUnavailable")]
	[InlineData(HttpStatusCode.InternalServerError, "SecureGate.Federation.DirectoryUnavailable")]
	public async Task Membros_ErroDoGraph_ETraduzidoSemVazarOCorpo(HttpStatusCode status, string code)
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(status, "{\"error\":{\"message\":\"detalhe-secreto\"}}"));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be(code);
		result.Error.Description.Should().NotContain("detalhe-secreto");
	}

	[Fact]
	public async Task Membros_ConsentimentoNaEmissaoDoToken_ConsentRequired()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\"}"));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.Error.Code.Should().Be("SecureGate.Federation.ConsentRequired");
	}

	[Fact]
	public async Task Membros_AppNaoConfigurada_NotEnabledSemChamarHttp()
	{
		var handler = new ScriptedHandler(_ => throw new InvalidOperationException("não deveria chamar"));

		var result = await Build(handler, configured: false).GetGroupMemberIdsAsync(DirectoryId, GroupId);

		result.Error.Code.Should().Be("SecureGate.Federation.NotEnabled");
		handler.Requests.Should().BeEmpty();
	}

	[Fact]
	public async Task Membros_GuidVazio_NaoChamaOGraph()
	{
		var handler = new ScriptedHandler(_ => throw new InvalidOperationException("não deveria chamar"));

		var result = await Build(handler).GetGroupMemberIdsAsync(DirectoryId, Guid.Empty);

		result.IsFailure.Should().BeTrue();
		handler.Requests.Should().BeEmpty();
	}

	// ---- A quais grupos o usuário pertence ----

	private static string[] SentGroupIds(string body) =>
		JsonDocument.Parse(body).RootElement.GetProperty("groupIds").EnumerateArray().Select(e => e.GetString()!).ToArray();

	[Fact]
	public async Task Pertinencia_PostaNoCheckMemberGroupsDoUsuario_ComOsIdsDosGrupos()
	{
		var groups = new[] { Guid.NewGuid(), Guid.NewGuid() };
		var handler = new ScriptedHandler(r => IsTokenRequest(r)
			? Json(HttpStatusCode.OK, TokenBody)
			: Json(HttpStatusCode.OK, $"{{\"value\":[\"{groups[1]:D}\"]}}"));

		var result = await Build(handler).CheckMemberGroupsAsync(DirectoryId, UserOid, groups);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEquivalentTo([groups[1]]);
		var call = handler.Requests.Single(r => !IsTokenRequest(r));
		call.Method.Should().Be(HttpMethod.Post);
		call.RequestUri!.ToString().Should().Be($"{GraphBase}users/{UserOid:D}/checkMemberGroups");
		SentGroupIds(handler.Bodies[call]).Should().BeEquivalentTo(groups.Select(g => g.ToString("D")));
	}

	[Fact]
	public async Task Pertinencia_QuarentaECincoGrupos_VaoEmTresLotesDeNoMaximoVinte()
	{
		var groups = Enumerable.Range(0, 45).Select(_ => Guid.NewGuid()).ToList();
		var batches = new List<string[]>();
		var handler = new ScriptedHandler(r =>
		{
			if (IsTokenRequest(r))
			{
				return Json(HttpStatusCode.OK, TokenBody);
			}

			var sent = SentGroupIds(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
			batches.Add(sent);

			// Cada lote devolve o primeiro grupo dele
			return Json(HttpStatusCode.OK, $"{{\"value\":[\"{sent[0]}\"]}}");
		});

		var result = await Build(handler).CheckMemberGroupsAsync(DirectoryId, UserOid, groups);

		batches.Select(b => b.Length).Should().Equal(20, 20, 5);
		batches.SelectMany(b => b).Should().BeEquivalentTo(groups.Select(g => g.ToString("D")));
		result.Value.Should().HaveCount(3);
	}

	[Fact]
	public async Task Pertinencia_UmLoteFalha_AFalhaE_DaRespostaInteira()
	{
		var groups = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();
		var calls = 0;
		var handler = new ScriptedHandler(r =>
		{
			if (IsTokenRequest(r))
			{
				return Json(HttpStatusCode.OK, TokenBody);
			}

			return ++calls == 1
				? Json(HttpStatusCode.OK, $"{{\"value\":[\"{groups[0]:D}\"]}}")
				: Json(HttpStatusCode.ServiceUnavailable, "fora");
		});

		var result = await Build(handler).CheckMemberGroupsAsync(DirectoryId, UserOid, groups);

		result.IsFailure.Should().BeTrue("o primeiro lote já tinha dado resultado, e mesmo assim nada parcial sai");
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryUnavailable");
	}

	[Fact]
	public async Task Pertinencia_IdAMaisNaResposta_NaoViraGrupoJustificado()
	{
		var asked = Guid.NewGuid();
		var surprise = Guid.NewGuid();
		var handler = new ScriptedHandler(r => IsTokenRequest(r)
			? Json(HttpStatusCode.OK, TokenBody)
			: Json(HttpStatusCode.OK, $"{{\"value\":[\"{asked:D}\",\"{surprise:D}\"]}}"));

		var result = await Build(handler).CheckMemberGroupsAsync(DirectoryId, UserOid, [asked]);

		result.Value.Should().BeEquivalentTo([asked]);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("{\"value\":[\"nao-e-guid\"]}")]
	[InlineData("{\"value\":[1]}")]
	public async Task Pertinencia_RespostaMalformada_Falha(string body)
	{
		var handler = new ScriptedHandler(r => IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, body));

		var result = await Build(handler).CheckMemberGroupsAsync(DirectoryId, UserOid, [Guid.NewGuid()]);

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
	}

	[Fact]
	public async Task Pertinencia_SemGrupos_NaoChamaOGraph()
	{
		var handler = new ScriptedHandler(_ => throw new InvalidOperationException("não deveria chamar"));

		var result = await Build(handler).CheckMemberGroupsAsync(DirectoryId, UserOid, []);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEmpty();
		handler.Requests.Should().BeEmpty();
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden, "SecureGate.Federation.ConsentRequired")]
	[InlineData(HttpStatusCode.NotFound, "SecureGate.Federation.DirectoryObjectNotFound")]
	[InlineData(HttpStatusCode.BadGateway, "SecureGate.Federation.DirectoryUnavailable")]
	public async Task Pertinencia_ErroDoGraph_ETraduzido(HttpStatusCode status, string code)
	{
		var handler = new ScriptedHandler(r => IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(status, "corpo"));

		var result = await Build(handler).CheckMemberGroupsAsync(DirectoryId, UserOid, [Guid.NewGuid()]);

		result.Error.Code.Should().Be(code);
	}

	[Fact]
	public async Task Pertinencia_AppNaoConfigurada_NotEnabled()
	{
		var handler = new ScriptedHandler(_ => throw new InvalidOperationException("não deveria chamar"));

		var result = await Build(handler, configured: false).CheckMemberGroupsAsync(DirectoryId, UserOid, [Guid.NewGuid()]);

		result.Error.Code.Should().Be("SecureGate.Federation.NotEnabled");
	}

	// ---- Perfil básico (provisionamento, ADR-0055 entrega B) ----

	private const string ProfileSelect = "id,displayName,mail,userPrincipalName";

	private static string ProfileJson(Guid id, string? name = "Ana", string? mail = "ana@secco.test", string? upn = "ana@secco.test") =>
		JsonSerializer.Serialize(new { id = id.ToString("D"), displayName = name, mail, userPrincipalName = upn });

	[Fact]
	public async Task MembrosComPerfil_PedeSoOsCamposFixos_ELeNomeEEmail()
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, $"{{\"value\":[{ProfileJson(UserOid)}]}}"));

		var result = await Build(handler).GetGroupMembersWithProfileAsync(DirectoryId, GroupId);

		handler.Requests.Single(r => !IsTokenRequest(r)).RequestUri!.ToString().Should().Be(
			$"{GraphBase}groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$select={ProfileSelect}&$top=999");
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().ContainSingle().Which.Should().Be(
			new DirectoryPerson(UserOid, "Ana", "ana@secco.test", "ana@secco.test"));
	}

	[Theory]
	[InlineData("{\"value\":[{\"id\":\"018f0000-0000-7000-8000-0000000000bb\",\"mail\":42}]}")]
	[InlineData("{\"value\":[{\"id\":\"018f0000-0000-7000-8000-0000000000bb\",\"displayName\":{\"x\":1}}]}")]
	public async Task MembrosComPerfil_CampoQueNaoETexto_FalhaComoLeituraIncompleta(string body)
	{
		var handler = new ScriptedHandler(r => IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, body));

		var result = await Build(handler).GetGroupMembersWithProfileAsync(DirectoryId, GroupId);

		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
	}

	// O reconciliador só segue sem provisionar quando o erro é Forbidden (falta de consentimento): qualquer outro
	// tipo aborta o tenant. A tradução do 403 precisa continuar sendo Forbidden
	[Fact]
	public async Task MembrosComPerfil_SemConsentimento_EForbidden()
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.Forbidden, "{\"error\":{}}"));

		var result = await Build(handler).GetGroupMembersWithProfileAsync(DirectoryId, GroupId);

		result.Error.Code.Should().Be("SecureGate.Federation.ConsentRequired");
		result.Error.Type.Should().Be(Secco.SharedKernel.Results.ErrorType.Forbidden);
	}

	[Fact]
	public async Task Pessoa_MontaAUrlComGuid_ELeOPerfil()
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, ProfileJson(UserOid, mail: null)));

		var result = await Build(handler).GetPersonAsync(DirectoryId, UserOid);

		handler.Requests.Single(r => !IsTokenRequest(r)).RequestUri!.ToString().Should().Be(
			$"{GraphBase}users/{UserOid:D}?$select={ProfileSelect}");
		result.Value.Should().Be(new DirectoryPerson(UserOid, "Ana", null, "ana@secco.test"));
	}

	[Fact]
	public async Task Pessoa_GraphDevolveOutroObjeto_FalhaSemAceitarOPerfilDeOutraPessoa()
	{
		var handler = new ScriptedHandler(r =>
			IsTokenRequest(r) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, ProfileJson(Guid.NewGuid())));

		var result = await Build(handler).GetPersonAsync(DirectoryId, UserOid);

		result.Error.Code.Should().Be("SecureGate.Federation.DirectoryReadIncomplete");
	}

	[Fact]
	public async Task Pessoa_GuidVazio_NaoChamaOGraph()
	{
		var handler = new ScriptedHandler(_ => throw new InvalidOperationException("não deveria chamar"));

		var result = await Build(handler).GetPersonAsync(DirectoryId, Guid.Empty);

		result.IsFailure.Should().BeTrue();
		handler.Requests.Should().BeEmpty();
	}

	private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public List<HttpRequestMessage> Requests { get; } = [];

		/// <summary>Corpo enviado em cada requisição (o conteúdo é descartado junto com ela).</summary>
		public Dictionary<HttpRequestMessage, string> Bodies { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request);

			if (request.Content is not null)
			{
				Bodies[request] = await request.Content.ReadAsStringAsync(cancellationToken);
			}

			return respond(request);
		}
	}

	/// <summary>Responde o token e depois nunca mais responde — até a requisição ser cancelada.</summary>
	private sealed class HangingHandler(string tokenBody) : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (IsTokenRequest(request))
			{
				return Json(HttpStatusCode.OK, tokenBody);
			}

			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

			return Json(HttpStatusCode.OK, "{}");
		}
	}
}
