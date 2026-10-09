using Secco.SDK.EntityFrameworkCore.Migrations;

namespace Secco.SecureGate.Infrastructure;

/// <summary>Migrations do banco de PLATAFORMA do SecureGate (ADR-0038).</summary>
internal sealed class SecureGatePlatformMigrator(IServiceProvider serviceProvider) : ISeccoDatabaseMigrator
{
	public string Name => "SecureGate (plataforma)";

	public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
	{
		await serviceProvider.MigrateSecureGateDatabaseAsync(cancellationToken).ConfigureAwait(false);
		return [];
	}
}
