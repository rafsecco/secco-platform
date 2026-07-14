using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Dados de criação de um usuário provisionado por administrador (Fase 6.5).</summary>
/// <param name="TenantId">Tenant ao qual o usuário pertence (ADR-0022: o registro carrega o tenant).</param>
/// <param name="Email">E-mail — também o username (único global; o tenant vem do registro no login).</param>
/// <param name="Password">Senha em claro; hasheada pelo Identity na Infrastructure. Nunca logar.</param>
/// <param name="Roles">Roles a atribuir no tenant (ADR-0021); devem existir.</param>
public sealed record CreateUserData(Guid TenantId, string Email, string Password, IReadOnlyList<string> Roles);

/// <summary>
/// Porta de provisionamento de usuários (ADR-0002): o hash de senha, a política e a
/// atribuição de roles são responsabilidade do ASP.NET Identity, que vive na Infrastructure.
/// A Application orquestra a validação de negócio e converte o resultado em <see cref="Result{T}"/>.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Cria o usuário e atribui os roles; mapeia falhas do Identity para <see cref="Error"/>.</summary>
    /// <param name="data">Dados de criação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<Result<UserDto>> CreateAsync(CreateUserData data, CancellationToken cancellationToken = default);

    /// <summary>Lista os usuários de um tenant com seus roles (sem segredos).</summary>
    /// <param name="tenantId">Tenant dono dos usuários.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<UserDto>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
