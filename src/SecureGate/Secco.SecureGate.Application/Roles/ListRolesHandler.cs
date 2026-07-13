using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Lista os roles de um tenant com suas permissões (visão de gestão/AdminPortal).</summary>
public sealed class ListRolesHandler(IRoleRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="tenantId">Tenant dono dos roles.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!await repository.TenantExistsAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return SecureGateErrors.Tenants.NotFound;
        }

        var roles = await repository.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return Result.Success(roles);
    }
}
