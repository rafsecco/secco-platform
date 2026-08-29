using Secco.LogStream.Infrastructure;
using Secco.SDK.Testing;

namespace Secco.LogStream.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV) sobre a
/// base da plataforma (ADR-0027), com dois tenants no catálogo apontando para bancos distintos
/// da mesma instância de SQL Server (ADR-0005).
/// </summary>
public sealed class LogStreamApiFactory : SeccoApiFactory<Program>
{
	/// <inheritdoc />
	protected override string Audience => "secco-logstream";

	/// <summary>Primeiro tenant do catálogo de testes.</summary>
	public Guid TenantAlfa { get; } = Guid.NewGuid();

	/// <summary>Segundo tenant — existe para provar que nenhuma query cruza bancos.</summary>
	public Guid TenantBeta { get; } = Guid.NewGuid();

	/// <inheritdoc />
	protected override Task MigrateAsync(IServiceProvider services) =>
		services.MigrateLogStreamTenantDatabasesAsync();

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		AddTenant(settings, TenantAlfa, GetConnectionStringFor("secco_logstream_alfa"));
		AddTenant(settings, TenantBeta, GetConnectionStringFor("secco_logstream_beta"));

		AddRolePermissions(
			settings,
			DefaultTestRole,
			"log-entries:read",
			"log-entries:write",
			"log-processes:read",
			"log-processes:write",
			"api-call-logs:read",
			"api-call-logs:write");
	}
}
