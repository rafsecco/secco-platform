using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Comando de substituição do conjunto de permissões de um role.</summary>
/// <param name="TenantId">Tenant dono do role.</param>
/// <param name="RoleName">Nome do role.</param>
/// <param name="Permissions">Conjunto COMPLETO desejado (PUT idempotente — revogar = omitir).</param>
public sealed record SetRolePermissionsCommand(Guid TenantId, string? RoleName, IReadOnlyList<string?>? Permissions);

/// <summary>
/// Substitui as permissões de um role (ADR-0021). Idempotente por design: o AdminPortal
/// envia o conjunto completo; revogação propaga aos produtos em até um TTL de cache.
/// </summary>
public sealed class SetRolePermissionsHandler(IRoleRepository repository)
{
    /// <summary>Limite de permissões por role (ADR-0020 — input externo com tamanho limitado).</summary>
    public const int MaxPermissionsPerRole = 200;

    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de substituição.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result> HandleAsync(
        SetRolePermissionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.RoleName?.Trim() ?? string.Empty;

        if (!RoleInputRules.IsValidName(name))
        {
            return Result.Failure(SecureGateErrors.Roles.NameInvalid);
        }

        // Nome reservado à plataforma: o role de operador não é gerido por API — seus poderes
        // vêm do scope admin, não de permissões (ADR-0023). Bloqueia em todos os tenants para
        // que o endpoint de gestão nunca toque essa estrutura (ADR-0020/0024).
        if (RoleInputRules.IsReservedName(name))
        {
            return Result.Failure(SecureGateErrors.Roles.NameReserved);
        }

        var permissions = command.Permissions ?? [];

        if (permissions.Count > MaxPermissionsPerRole)
        {
            return Result.Failure(SecureGateErrors.Roles.TooManyPermissions);
        }

        var normalized = new HashSet<string>(StringComparer.Ordinal);

        foreach (var permission in permissions)
        {
            if (!SeccoPermissions.IsValid(permission))
            {
                return Result.Failure(SecureGateErrors.Roles.PermissionInvalid);
            }

            normalized.Add(permission!);
        }

        if (!await repository.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(SecureGateErrors.Tenants.NotFound);
        }

        var replaced = await repository.ReplacePermissionsAsync(command.TenantId, name, normalized, cancellationToken)
            .ConfigureAwait(false);

        return replaced
            ? Result.Success()
            : Result.Failure(SecureGateErrors.Roles.NotFound);
    }
}
