using System.Net.Mail;
using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Comando de criação de usuário (provisionamento por administrador).</summary>
/// <param name="TenantId">Tenant ao qual o usuário pertence.</param>
/// <param name="Email">E-mail (também o username). Obrigatório.</param>
/// <param name="Password">Senha em claro. Obrigatória.</param>
/// <param name="Roles">Roles a atribuir no tenant (opcional).</param>
public sealed record CreateUserCommand(Guid TenantId, string? Email, string? Password, IReadOnlyList<string>? Roles);

/// <summary>
/// Cria um usuário no tenant (Fase 6.5). Valida e-mail, senha e a existência do tenant e
/// dos roles ANTES de acionar o Identity (ADR-0020: nada não confiável chega ao provedor
/// sem validação de formato). O hash de senha e a política ficam a cargo do Identity.
/// </summary>
public sealed class CreateUserHandler(IRoleRepository roleRepository, IUserDirectory userDirectory)
{
    /// <summary>Tamanho máximo aceito para o e-mail.</summary>
    private const int EmailMaxLength = 256;

    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de criação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<UserDto>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var email = command.Email?.Trim() ?? string.Empty;

        if (email.Length is 0 or > EmailMaxLength || !MailAddress.TryCreate(email, out _))
        {
            return Result.Failure<UserDto>(SecureGateErrors.Users.EmailInvalid);
        }

        if (string.IsNullOrEmpty(command.Password))
        {
            return Result.Failure<UserDto>(SecureGateErrors.Users.PasswordRequired);
        }

        if (!await roleRepository.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<UserDto>(SecureGateErrors.Tenants.NotFound);
        }

        var roles = command.Roles ?? [];

        foreach (var role in roles)
        {
            if (!await roleRepository.RoleExistsAsync(command.TenantId, role, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<UserDto>(SecureGateErrors.Users.RoleNotFound);
            }
        }

        return await userDirectory
            .CreateAsync(new CreateUserData(command.TenantId, email, command.Password, roles), cancellationToken)
            .ConfigureAwait(false);
    }
}
