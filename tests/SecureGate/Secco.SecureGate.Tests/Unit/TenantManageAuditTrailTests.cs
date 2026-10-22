using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Infrastructure.Elevation;
using Secco.SecureGate.Infrastructure.TenantAdministration;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Trilha da gestão do tenant por máquina (ADR-0052) no LogStream: o ator é o client, não uma pessoa, e a ação
/// tem prefixo próprio — senão a trilha confundiria a máquina com um admin de tenant.
/// </summary>
public class TenantManageAuditTrailTests
{
	private static readonly Guid Tenant = Guid.CreateVersion7();

	[Fact]
	public async Task GestaoDoTenant_GravaComoClient_ComPrefixoProprio()
	{
		var handler = new RecordingHandler();
		var record = new TenantAdminAuditRecord(
			UserId: null, UserName: null, Tenant, ClientId: "cli_intranet0000000", Operation: "CreateRole",
			TargetUserId: null, AdministeredTenantId: Tenant, Authority: SecureGateScopes.TenantManage);

		var accepted = await CreateAuditor(handler).RecordAsync(record);

		accepted.Should().BeTrue();
		handler.Request!.Headers.GetValues("X-Tenant-Id").Should().Equal(Tenant.ToString());
		using var body = JsonDocument.Parse(handler.Body!);
		body.RootElement.GetProperty("actorId").GetString().Should().Be("cli_intranet0000000");
		IsClient(body.RootElement.GetProperty("actorType")).Should().BeTrue("quem agiu é a máquina");
		body.RootElement.GetProperty("action").GetString()
			.Should().Be(LogStreamTenantAdminAuditor.TenantManageActionPrefix + "CreateRole");
	}

	[Fact]
	public async Task GestaoDoTenant_SemClient_NaoGravaEDevolveFalse()
	{
		var handler = new RecordingHandler();
		var record = new TenantAdminAuditRecord(
			UserId: null, UserName: null, Tenant, ClientId: null, Operation: "CreateRole",
			TargetUserId: null, AdministeredTenantId: Tenant, Authority: SecureGateScopes.TenantManage);

		(await CreateAuditor(handler).RecordAsync(record)).Should().BeFalse("sem ator não há registro, e sem registro não há escrita");
		handler.Request.Should().BeNull();
	}

	[Fact]
	public async Task AdminDeTenant_ContinuaGravandoAPessoa()
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

	private static LogStreamTenantAdminAuditor CreateAuditor(HttpMessageHandler handler)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(LogStreamElevationAuditor.HttpClientName)
			.Returns(_ => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://logstream.test") });

		return new LogStreamTenantAdminAuditor(factory, NullLogger<LogStreamTenantAdminAuditor>.Instance);
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
