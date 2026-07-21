using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>Leitura pontual de um tenant com os produtos que têm banco cadastrado.</summary>
public sealed class GetTenantHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<TenantDetailDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return SecureGateErrors.Tenants.NotFound;
        }

        var products = await repository.ListDatabaseProductsAsync(id, cancellationToken).ConfigureAwait(false);

        var federation = await repository.GetFederationAsync(id, cancellationToken).ConfigureAwait(false);

        return new TenantDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.IsActive,
            tenant.CreatedAt,
            products,
            federation is null ? null : TenantFederationDto.FromEntity(federation));
    }
}
