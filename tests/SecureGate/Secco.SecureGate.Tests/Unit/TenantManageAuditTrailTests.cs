using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Infrastructure.Elevation;
using Secco.SecureGate.Infrastructure.Tenancy;
using Secco.SecureGate.Infrastructure.TenantAdministration;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Trilha da gestão do tenant por máquina (ADR-0052/0054): grava no banco do SecureGate como client, no escopo do
/// tenant, com a Metadata montada pelo servidor; a trilha do admin de tenant (ADR-0044) segue no LogStream.
/// </summary>
public class TenantManageAuditTrailTests
{
	private static readonly Guid Tenant = Guid.CreateVersion7();

	[Fact]
	public async Task GestaoDoTenant_GravaLocalComoClient_NoEscopoDoTenant_ComInstanteDoServidor()
	{
		var scope = TenantAccessScope.Detached();
		var repository = Substitute.For<IAuthorityAuditRepository>();
		AuthorityAuditEntry? saved = null;
		TenantAccessMode? modeAtWrite = null;
		Guid? tenantAtWrite = null;
		repository.AddAsync(Arg.Do<AuthorityAuditEntry>(entry => saved = entry), Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				modeAtWrite = scope.Mode;
				tenantAtWrite = scope.TenantId;

				return Task.CompletedTask;
			});
		var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
		var auditor = new DatabaseTenantManageAuditor(
			repository, scope, new FixedClock(now), NullLogger<DatabaseTenantManageAuditor>.Instance);
		var target = Guid.CreateVersion7();

		var accepted = await auditor.RecordAsync(new TenantManageAuditRecord(Tenant, "cli_intranet0000000", "AddUserRole", target));

		accepted.Should().BeTrue();
		modeAtWrite.Should().Be(TenantAccessMode.Tenant, "a gravação roda no escopo do tenant, nunca em modo plataforma");
		tenantAtWrite.Should().Be(Tenant);
		scope.Mode.Should().Be(TenantAccessMode.None, "o escopo volta ao que era");
		saved.Should().NotBeNull();
		saved!.TenantId.Should().Be(Tenant);
		saved.ActorType.Should().Be("client");
		saved.ActorId.Should().Be("cli_intranet0000000");
		saved.Action.Should().Be("gestao-do-tenant.AddUserRole");
		saved.ResourceType.Should().Be("user");
		saved.ResourceId.Should().Be(target.ToString());
		saved.CreatedAt.Should().Be(now);
		using var metadata = JsonDocument.Parse(saved.Metadata!);
		metadata.RootElement.GetProperty("authority").GetString().Should().Be(SecureGateScopes.TenantManage);
		metadata.RootElement.GetProperty("act").GetString().Should().Be("cli_intranet0000000");
		metadata.RootElement.GetProperty("tenantId").GetGuid().Should().Be(Tenant);
	}

	[Fact]
	public async Task GestaoDoTenant_SemClient_NaoGravaEDevolveFalse()
	{
		var repository = Substitute.For<IAuthorityAuditRepository>();
		var auditor = CreateDatabaseAuditor(repository);

		(await auditor.RecordAsync(new TenantManageAuditRecord(Tenant, null, "CreateRole", null)))
			.Should().BeFalse("sem ator não há registro, e sem registro não há escrita");
		await repository.DidNotReceive().AddAsync(Arg.Any<AuthorityAuditEntry>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GestaoDoTenant_FalhaDeGravacao_DevolveFalse_SemLancar()
	{
		var repository = Substitute.For<IAuthorityAuditRepository>();
		repository.AddAsync(Arg.Any<AuthorityAuditEntry>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("Server=segredo;Password=x")));

		(await CreateDatabaseAuditor(repository).RecordAsync(new TenantManageAuditRecord(Tenant, "cli_x", "CreateRole", null)))
			.Should().BeFalse();
	}

	[Fact]
	public async Task GestaoDoTenant_ClientAcimaDoLimite_NaoGrava_EDevolveFalse()
	{
		var repository = Substitute.For<IAuthorityAuditRepository>();

		var accepted = await CreateDatabaseAuditor(repository).RecordAsync(
			new TenantManageAuditRecord(Tenant, new string('c', AuthorityAuditEntry.ActorIdMaxLength + 1), "CreateRole", null));

		accepted.Should().BeFalse("o limite de tamanho do domínio vale: entrada inválida não é gravada");
		await repository.DidNotReceive().AddAsync(Arg.Any<AuthorityAuditEntry>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task AdminDeTenant_ContinuaGravandoAPessoaNoLogStream()
	{
		var handler = new RecordingHandler();
		var person = Guid.CreateVersion7();
		var record = new TenantAdminAuditRecord(person, "Ana", Tenant, "intranet-web", "CreateRole", null, Tenant);

		(await CreateAuditor(handler).RecordAsync(record)).Should().BeTrue();

		using var body = JsonDocument.Parse(handler.Body!);
		body.RootElement.GetProperty("actorId").GetString().Should().Be(person.ToString());
		IsClient(body.RootElement.GetProperty("actorType")).Should().BeFalse();
		body.RootElement.GetProperty("action").GetString().Should().Be(LogStreamTenantAdminAuditor.ActionPrefix + "CreateRole");
	}

	// O enum pode sair como texto ou número, conforme o conversor do client
	private static bool IsClient(JsonElement actorType) => actorType.ValueKind == JsonValueKind.String
		? string.Equals(actorType.GetString(), "client", StringComparison.OrdinalIgnoreCase)
		: actorType.GetInt32() == 1;

	private static DatabaseTenantManageAuditor CreateDatabaseAuditor(IAuthorityAuditRepository repository) => new(
		repository, TenantAccessScope.Detached(), TimeProvider.System, NullLogger<DatabaseTenantManageAuditor>.Instance);

	private static LogStreamTenantAdminAuditor CreateAuditor(HttpMessageHandler handler)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(LogStreamElevationAuditor.HttpClientName)
			.Returns(_ => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://logstream.test") });

		return new LogStreamTenantAdminAuditor(factory, NullLogger<LogStreamTenantAdminAuditor>.Instance);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private sealed class RecordingHandler : HttpMessageHandler
	{
		public HttpRequestMessage? Request { get; private set; }

		public string? Body { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Request = request;
			Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

			return new HttpResponseMessage(HttpStatusCode.Created)
			{
				Content = new StringContent("""{"id":"018f0000-0000-7000-8000-000000000001"}""", Encoding.UTF8, "application/json"),
			};
		}
	}
}
