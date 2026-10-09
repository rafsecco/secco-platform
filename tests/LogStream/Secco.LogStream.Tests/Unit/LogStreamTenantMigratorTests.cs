using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.LogStream.Infrastructure;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class LogStreamTenantMigratorTests
{
	private sealed class FixedCatalog(params TenantInfo[] tenants) : ITenantCatalog
	{
		public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(tenants.FirstOrDefault(tenant => tenant.TenantId == tenantId));

		public ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<TenantInfo>>(tenants);
	}

	[Fact]
	public async Task MigrateAsync_SemTenantsNoCatalogo_DevolveVazioSemExcecao()
	{
		var catalog = new FixedCatalog();
		var migrator = new LogStreamTenantMigrator(
			catalog, Options.Create(new LogStreamDatabaseOptions()), NullLogger<LogStreamTenantMigrator>.Instance);

		var failures = await migrator.MigrateAsync();

		failures.Should().BeEmpty();
	}

	[Fact]
	public async Task MigrateAsync_TenantComBancoInacessivel_DevolveIdDoTenantENuncaAConnectionString()
	{
		var tenantId = Guid.NewGuid();
		var catalog = new FixedCatalog(new TenantInfo(
			tenantId, "Server=127.0.0.1,1;Database=x;User Id=u;Password=segredo-nao-pode-vazar;Connect Timeout=1;Encrypt=False"));
		var migrator = new LogStreamTenantMigrator(
			catalog, Options.Create(new LogStreamDatabaseOptions()), NullLogger<LogStreamTenantMigrator>.Instance);

		var failures = await migrator.MigrateAsync();

		failures.Should().ContainSingle().Which.Should().Be(tenantId.ToString());
	}
}
