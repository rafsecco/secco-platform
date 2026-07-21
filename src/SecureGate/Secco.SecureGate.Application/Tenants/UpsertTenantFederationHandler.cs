using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>Comando de cadastro/atualização da federação de autenticação de um tenant (ADR-0026).</summary>
/// <param name="TenantId">Tenant dono da federação.</param>
/// <param name="DirectoryId">Directory id (tenant do Entra ID da empresa). Não é segredo.</param>
/// <param name="Enabled">Habilita ou desabilita o login federado do tenant.</param>
public sealed record UpsertTenantFederationCommand(Guid TenantId, Guid DirectoryId, bool Enabled);

/// <summary>
/// Upsert idempotente da federação Entra ID de um tenant (ADR-0026). A federação é opt-in e
/// 1:1 com o tenant: um novo PUT atualiza o directory id e o estado de habilitação. O directory
/// id não é segredo (é o <c>tid</c> esperado do token), então a leitura o devolve no detalhe.
/// </summary>
public sealed class UpsertTenantFederationHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de upsert.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result> HandleAsync(
        UpsertTenantFederationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.DirectoryId == Guid.Empty)
        {
            return Result.Failure(SecureGateErrors.Federation.DirectoryIdRequired);
        }

        var tenant = await repository.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return Result.Failure(SecureGateErrors.Tenants.NotFound);
        }

        var existing = await repository.GetFederationAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            var federation = new TenantFederation(command.TenantId, command.DirectoryId);
            federation.SetEnabled(command.Enabled);
            await repository.AddFederationAsync(federation, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.UpdateDirectory(command.DirectoryId);
            existing.SetEnabled(command.Enabled);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
