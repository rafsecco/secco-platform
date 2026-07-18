using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Comando de criação de role em um tenant.</summary>
/// <param name="TenantId">Tenant dono do role.</param>
/// <param name="Name">Nome do role. Obrigatório, sem espaços.</param>
public sealed record CreateRoleCommand(Guid TenantId, string? Name);

/// <summary>Cria um role no tenant (nasce sem permissões — concedidas via PUT idempotente).</summary>
public sealed class CreateRoleHandler(IRoleRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de criação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<RoleDto>> HandleAsync(
        CreateRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name?.Trim() ?? string.Empty;

        if (!RoleInputRules.IsValidName(name))
        {
            return SecureGateErrors.Roles.NameInvalid;
        }

        // Nome reservado à plataforma: bloqueia em TODOS os tenants (o role legítimo vem do
        // seed de referência, que não passa por aqui) — defesa contra escalonamento por
        // colisão de nome (ADR-0020/0023/0024).
        if (RoleInputRules.IsReservedName(name))
        {
            return SecureGateErrors.Roles.NameReserved;
        }

        if (!await repository.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return SecureGateErrors.Tenants.NotFound;
        }

        if (await repository.RoleExistsAsync(command.TenantId, name, cancellationToken).ConfigureAwait(false))
        {
            return SecureGateErrors.Roles.AlreadyExists;
        }

        await repository.CreateRoleAsync(command.TenantId, name, cancellationToken).ConfigureAwait(false);

        return new RoleDto(name, []);
    }
}
