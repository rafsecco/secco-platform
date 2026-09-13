using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Authorization;
using Secco.SecureGate.Application.Elevation;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Infrastructure.Elevation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Política da elevação (ADR-0031) nas peças que não precisam de host: teto do TTL, read-sets
/// cross-tenant, validação da identidade de auditoria e o auditor que grava no LogStream.
/// </summary>
public class ElevationPolicyTests
{
	private static readonly Guid AnyTenant = Guid.Parse("018f0000-0000-7000-8000-00000000e1e1");

	// ─────────────────────────────── TTL (invariante 3) ───────────────────────────────

	[Theory]
	[InlineData(600, 60)]
	[InlineData(61, 60)]
	[InlineData(60, 60)]
	[InlineData(15, 15)]
	[InlineData(1, 1)]
	[InlineData(0, 1)]
	[InlineData(-30, 1)]
	public void EffectiveTokenLifetime_RespeitaPisoETeto(int configured, int expectedMinutes)
	{
		var options = new ElevationOptions { TokenLifetimeMinutes = configured };

		// Acima do teto cai no teto; não positivo cai no PISO, e não no padrão — configuração
		// inválida resulta em token mais curto, que é o lado seguro
		options.EffectiveTokenLifetime.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
	}

	[Fact]
	public void EffectiveTokenLifetime_SemConfiguracao_UsaOPadraoDe15Minutos() =>
		new ElevationOptions().EffectiveTokenLifetime.Should().Be(SecureGatePlatform.ElevatedTokenDefaultLifetime);

	// ─────────────────────────────── read-sets cross-tenant ───────────────────────────────

	[Fact]
	public async Task RolePermissions_LeitorElevado_RecebeDiagnosticoSemTrilhaDeAuditoria()
	{
		var repository = Substitute.For<IRoleRepository>();

		var result = await new GetRolePermissionsHandler(repository)
			.HandleAsync(AnyTenant, SecureGatePlatform.ElevatedLogReaderRole);

		result.Value.Should().BeEquivalentTo(["log-entries:read", "log-processes:read", "api-call-logs:read"]);
		result.Value.Should().NotContain("audit-entries:read",
			"quem só elevou para diagnóstico não lê a trilha de auditoria de outro tenant");
		await repository.DidNotReceiveWithAnyArgs().GetPermissionsAsync(default, default!, default);
	}

	[Fact]
	public async Task RolePermissions_Auditor_RecebeSomenteEscritaDeAuditoria()
	{
		var result = await new GetRolePermissionsHandler(Substitute.For<IRoleRepository>())
			.HandleAsync(AnyTenant, SecureGatePlatform.AuditorRole);

		// A única escrita cross-tenant da plataforma: uma permissão, nenhuma leitura
		result.Value.Should().Equal(["audit-entries:write"]);
		result.Value.Should().NotContain(permission => permission.EndsWith(":read", StringComparison.Ordinal));
	}

	[Fact]
	public async Task RolePermissions_Operador_SegueComATrilhaDeAuditoria()
	{
		var result = await new GetRolePermissionsHandler(Substitute.For<IRoleRepository>())
			.HandleAsync(AnyTenant, SecureGatePlatform.OperatorRole);

		// Regressão: o read-set do operador não pode ter sido estreitado junto com o do leitor elevado
		result.Value.Should().Contain("audit-entries:read");
	}

	[Fact]
	public void ReadSetDoLeitorElevado_EhSubconjuntoEstritoDoOperador()
	{
		SecureGatePlatform.ElevatedLogReaderPermissions.Should().BeSubsetOf(SecureGatePlatform.OperatorReadPermissions);
		SecureGatePlatform.ElevatedLogReaderPermissions.Should().HaveCountLessThan(
			SecureGatePlatform.OperatorReadPermissions.Count,
			"o leitor elevado tem de ver MENOS que o operador, nunca o mesmo");
	}

	[Fact]
	public void NenhumPapelCrossTenantCombinaLeituraEEscrita()
	{
		// Guarda contra deriva futura: um papel cross-tenant só lê OU só escreve
		SecureGatePlatform.ElevatedLogReaderPermissions.Should().OnlyContain(p => p.EndsWith(":read", StringComparison.Ordinal));
		SecureGatePlatform.OperatorReadPermissions.Should().OnlyContain(p => p.EndsWith(":read", StringComparison.Ordinal));
		SecureGatePlatform.AuditorPermissions.Should().OnlyContain(p => p.EndsWith(":write", StringComparison.Ordinal));
	}

	// ─────────────────────── identidade de auditoria (emenda da ADR-0031) ───────────────────────

	[Fact]
	public void AuditOptions_SemNenhumaChave_EhValidaEDesligaAElevacao()
	{
		var options = new ElevationAuditOptions();

		options.IsConfigured.Should().BeFalse();
		options.TryValidate(out var error).Should().BeTrue("seção ausente é válida: significa elevação desligada");
		error.Should().BeNull();
	}

	[Fact]
	public void AuditOptions_ParcialmenteConfigurada_EhInvalidaSemCitarOSegredo()
	{
		const string Secret = "segredo-que-nao-pode-vazar-em-log";
		var options = new ElevationAuditOptions { ClientSecret = Secret };

		options.IsConfigured.Should().BeTrue("uma chave declarada já é intenção de ligar");
		options.TryValidate(out var error).Should().BeFalse();

		error.Should().Contain(nameof(ElevationAuditOptions.LogStreamBaseUrl))
			.And.Contain(nameof(ElevationAuditOptions.AuthorityUrl))
			.And.Contain(nameof(ElevationAuditOptions.ClientId));
		error.Should().NotContain(Secret, "a mensagem de erro lista NOMES de chave, nunca valores (ADR-0020)");
	}

	[Fact]
	public void AuditOptions_ComUrlNaoHttp_EhInvalida()
	{
		var options = FullOptions();
		options.LogStreamBaseUrl = "ftp://logstream.interno";

		options.TryValidate(out _).Should().BeFalse();
	}

	[Fact]
	public void AuditOptions_Completa_EhValida() => FullOptions().TryValidate(out _).Should().BeTrue();

	// ─────────────────────────────── auditor do LogStream ───────────────────────────────

	[Fact]
	public async Task Auditor_ComRegistroAceito_GravaNoTenantDeQuemElevouEDevolveTrue()
	{
		var handler = new RecordingHandler(HttpStatusCode.Created, """{"id":"018f0000-0000-7000-8000-000000000001"}""");
		var auditor = CreateAuditor(handler);
		var record = SampleRecord();

		var accepted = await auditor.RecordAsync(record);

		accepted.Should().BeTrue();
		handler.Request!.Method.Should().Be(HttpMethod.Post);
		handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/audit-entries");
		handler.Request.Headers.GetValues("X-Tenant-Id").Should().Equal(record.TenantId.ToString());
		handler.Body.Should().Contain(record.UserId.ToString()).And.Contain(LogStreamElevationAuditor.AuditAction);
	}

	[Theory]
	[InlineData(HttpStatusCode.InternalServerError)]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.ServiceUnavailable)]
	public async Task Auditor_ComRespostaDeFalha_DevolveFalse(HttpStatusCode status)
	{
		var auditor = CreateAuditor(new RecordingHandler(status, "{}"));

		(await auditor.RecordAsync(SampleRecord())).Should().BeFalse("sem registro aceito, não há troca");
	}

	[Fact]
	public async Task Auditor_ComFalhaDeRede_DevolveFalseSemLancar()
	{
		var auditor = CreateAuditor(new ThrowingHandler(new HttpRequestException("conexão recusada")));

		(await auditor.RecordAsync(SampleRecord())).Should().BeFalse();
	}

	[Fact]
	public async Task Auditor_ComRequisicaoCancelada_PropagaOCancelamento()
	{
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		var auditor = CreateAuditor(new RecordingHandler(HttpStatusCode.Created, "{}"));

		var act = () => auditor.RecordAsync(SampleRecord(), cancellation.Token);

		await act.Should().ThrowAsync<OperationCanceledException>(
			"cancelamento do chamador não é falha de auditoria — é a requisição que acabou");
	}

	[Fact]
	public async Task AuditorNaoConfigurado_NaoAuditaEDesligaAElevacao()
	{
		var auditor = NotConfiguredElevationAuditor.Instance;

		auditor.IsConfigured.Should().BeFalse();
		(await auditor.RecordAsync(SampleRecord())).Should().BeFalse();
	}

	private static ElevationAuditOptions FullOptions() => new()
	{
		LogStreamBaseUrl = "https://logstream.interno",
		AuthorityUrl = "https://securegate.interno",
		ClientId = "secco-elevation-auditor",
		ClientSecret = "segredo",
	};

	private static ElevationAuditRecord SampleRecord() => new(
		Guid.CreateVersion7(), Guid.CreateVersion7(), "pessoa@secco.test", "elevation-webapp",
		["logstream"], DateTimeOffset.UtcNow.AddMinutes(15));

	private static LogStreamElevationAuditor CreateAuditor(HttpMessageHandler handler)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(LogStreamElevationAuditor.HttpClientName)
			.Returns(_ => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://logstream.test") });

		return new LogStreamElevationAuditor(factory, NullLogger<LogStreamElevationAuditor>.Instance);
	}

	private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
	{
		public HttpRequestMessage? Request { get; private set; }

		public string? Body { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			Request = request;
			Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

			return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		}
	}

	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw exception;
	}
}
