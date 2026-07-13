using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>
/// Lista os tenants do catálogo (visão de gestão, sem connection strings).
/// Lista completa por design: o catálogo é pequeno e limitado por natureza —
/// paginação entra com o AdminPortal se houver demanda real.
/// </summary>
public sealed class ListTenantsHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<IReadOnlyList<TenantDto>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await repository.ListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success<IReadOnlyList<TenantDto>>([.. tenants.Select(TenantDto.FromEntity)]);
    }
}
