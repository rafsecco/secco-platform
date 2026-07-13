using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Authorization;

/// <summary>
/// Resolução <c>(tenant, role) → permissões</c> servida aos produtos (ADR-0021) — o
/// caminho quente do <c>IPermissionResolver</c> remoto do SDK. Role desconhecido responde
/// lista VAZIA (não 404): para autorização, role inexistente e role sem permissões são
/// equivalentes — e a resposta não revela o modelo de roles do tenant (ADR-0020).
/// </summary>
public sealed class GetRolePermissionsHandler(IRoleRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="tenantId">Tenant do contexto do produto chamador.</param>
    /// <param name="roleName">Nome do role (claim curta <c>role</c> do token).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(
        Guid tenantId,
        string? roleName,
        CancellationToken cancellationToken = default)
    {
        var name = roleName?.Trim() ?? string.Empty;

        if (!RoleInputRules.IsValidName(name))
        {
            return SecureGateErrors.Roles.NameInvalid;
        }

        var permissions = await repository.GetPermissionsAsync(tenantId, name, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(permissions ?? []);
    }
}
