using Secco.SecureGate.Application.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Provisioning;

/// <summary>Estado de um banco de tenant em um produto.</summary>
/// <param name="Product">Produto ao qual o banco pertence.</param>
/// <param name="Reachable">Indica se o banco respondeu à sondagem.</param>
/// <param name="FailureReason">Classificação da falha, quando inalcançável. Nunca a exceção crua.</param>
/// <param name="CheckedAt">Momento da verificação.</param>
public sealed record TenantDatabaseStatusDto(
	string Product,
	bool Reachable,
	string? FailureReason,
	DateTimeOffset CheckedAt);

/// <summary>
/// Painel dos bancos de um tenant: o que está cadastrado e o que responde.
/// </summary>
/// <remarks>
/// <b>Não usa credencial privilegiada nenhuma</b> — foi a observação do adotante na issue #3 e ela
/// se sustenta: o catálogo já sabe tenant e banco, e o estado vivo sai de uma sondagem com a
/// <i>conexão de runtime</i> de cada tenant. Metadados de servidor (bancos órfãos, espaço em
/// disco) pediriam mais que isso e ficaram fora de escopo.
/// <para>
/// A connection string nunca sai daqui: ela é lida, usada para abrir a conexão e descartada. A
/// resposta carrega produto, alcançabilidade e classificação de falha — nada mais (ADR-0020).
/// </para>
/// </remarks>
public sealed class GetTenantDatabaseStatusHandler(
	ITenantRepository repository,
	IEnumerable<ITenantDatabaseHealthProbe> probes,
	TenantDatabaseProvisioningOptions options)
{
	/// <summary>Tempo máximo por banco — um banco fora do ar não pode pendurar a página.</summary>
	private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant a inspecionar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantDatabaseStatusDto>>> HandleAsync(
		Guid tenantId,
		CancellationToken cancellationToken = default)
	{
		var tenant = await repository.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (tenant is null)
		{
			return Result.Failure<IReadOnlyList<TenantDatabaseStatusDto>>(SecureGateErrors.Tenants.NotFound);
		}

		var products = await repository.ListDatabaseProductsAsync(tenantId, cancellationToken).ConfigureAwait(false);
		var statuses = new List<TenantDatabaseStatusDto>(products.Count);

		foreach (var product in products)
		{
			var database = await repository.GetDatabaseAsync(tenantId, product, cancellationToken)
				.ConfigureAwait(false);

			statuses.Add(database is null
				? new TenantDatabaseStatusDto(product, false, "não cadastrado", DateTimeOffset.UtcNow)
				: await ProbeAsync(product, database.ConnectionString, cancellationToken).ConfigureAwait(false));
		}

		return Result.Success<IReadOnlyList<TenantDatabaseStatusDto>>(statuses);
	}

	private async Task<TenantDatabaseStatusDto> ProbeAsync(
		string product,
		string connectionString,
		CancellationToken cancellationToken)
	{
		var providerName = options.Resolve(TenantDatabaseProvisioningOptions.DefaultTargetName)?.Provider
			?? SecureGateDatabaseProviderNames.SqlServer;

		var probe = probes.FirstOrDefault(candidate =>
			string.Equals(candidate.Provider, providerName, StringComparison.OrdinalIgnoreCase));

		if (probe is null)
		{
			return new TenantDatabaseStatusDto(product, false, "provider sem sonda registrada", DateTimeOffset.UtcNow);
		}

		var result = await probe.ProbeAsync(connectionString, ProbeTimeout, cancellationToken).ConfigureAwait(false);

		return new TenantDatabaseStatusDto(product, result.Reachable, result.FailureReason, DateTimeOffset.UtcNow);
	}
}
