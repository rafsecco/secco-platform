using Secco.SampleService.Infrastructure;
using Secco.SDK.Testing;

namespace Secco.SampleService.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV) sobre a
/// base de testes da plataforma (ADR-0027), com dois tenants no catálogo apontando para bancos
/// distintos (ADR-0005).
/// <para>
/// A base cuida da instância de SQL Server, da chave de assinatura aleatória e da chamada única
/// das migrations — o produto só declara o que é dele.
/// </para>
/// </summary>
public sealed class SampleServiceApiFactory : SeccoApiFactory<Program>
{
	/// <inheritdoc />
	protected override string Audience => "secco-sampleservice";

	/// <summary>Primeiro tenant do catálogo de testes.</summary>
	public Guid TenantAlfa { get; } = Guid.NewGuid();

	/// <summary>Segundo tenant — existe para provar que nenhuma query cruza bancos.</summary>
	public Guid TenantBeta { get; } = Guid.NewGuid();

	/// <inheritdoc />
	protected override Task MigrateAsync(IServiceProvider services) =>
		services.MigrateSampleServiceTenantDatabasesAsync();

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		AddTenant(settings, TenantAlfa, GetConnectionStringFor("secco_sampleservice_alfa"));
		AddTenant(settings, TenantBeta, GetConnectionStringFor("secco_sampleservice_beta"));

		// Permissões do role dos tokens de teste (ADR-0021) — resolver por configuração
		AddRolePermissions(settings, DefaultTestRole, "samples:read", "samples:write");
	}
}
