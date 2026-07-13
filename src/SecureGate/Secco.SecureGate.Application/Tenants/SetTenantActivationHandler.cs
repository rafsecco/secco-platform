using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>
/// Ativa/desativa um tenant. Desativar remove o tenant do catálogo servido aos produtos —
/// a resolução de tenant para de funcionar em no máximo um TTL de cache dos consumidores.
/// </summary>
public sealed class SetTenantActivationHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador do tenant.</param>
    /// <param name="active"><c>true</c> para ativar; <c>false</c> para desativar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result> HandleAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var tenant = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return Result.Failure(SecureGateErrors.Tenants.NotFound);
        }

        if (active)
        {
            tenant.Activate();
        }
        else
        {
            tenant.Deactivate();
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
