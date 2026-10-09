using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SampleService.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Migrations;

namespace Secco.SampleService.Infrastructure;

/// <summary>
/// Migrations de TODOS os tenants do catálogo (ADR-0038). Um tenant que falha não interrompe os
/// demais; o alvo devolvido é o id do tenant, nunca a connection string.
/// </summary>
internal sealed partial class SampleServiceTenantMigrator(
	ITenantCatalog catalog,
	IOptions<SampleServiceDatabaseOptions> databaseOptions,
	ILogger<SampleServiceTenantMigrator> logger) : ISeccoDatabaseMigrator
{
	public string Name => "SampleService (tenants)";

	public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
	{
		var failures = new List<string>();

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			try
			{
				var options = SampleServiceDatabaseProviderConfigurator.CreateOptions(
					databaseOptions.Value.Provider, tenant.ConnectionString);

				// Sem interceptor de primeiro uso: este contexto só serve para migrar
				await using var context = new SampleServiceDbContext(options);
				await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
			}
#pragma warning disable CA1031 // Falha de um tenant não interrompe os demais; logada e contada
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				LogTenantFailed(logger, tenant.TenantId, exception.Message);
				failures.Add(tenant.TenantId.ToString());
			}
#pragma warning restore CA1031
		}

		return failures;
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Migrations do SampleService falharam no tenant {TenantId}: {Reason}")]
	private static partial void LogTenantFailed(ILogger logger, Guid tenantId, string reason);
}
