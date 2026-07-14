using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>Provisionamento de usuários de um tenant, on-behalf-of o operador (Fase 7.2).</summary>
public interface IUserAdminService
{
    /// <summary>Lista os usuários do tenant com seus roles.</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<UserSummary>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Cria um usuário no tenant (senha hasheada no servidor pelo SecureGate).</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="email">E-mail (também o username).</param>
    /// <param name="password">Senha inicial.</param>
    /// <param name="roles">Roles a atribuir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task CreateUserAsync(
        Guid tenantId, string email, string password, IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class SecureGateUserAdminService(ISecureGateClientFactory clientFactory) : IUserAdminService
{
    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var users = await client.ListUsersAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return [.. users.Select(user => new UserSummary(user.Id, user.Email, [.. user.Roles]))];
    }

    public async Task CreateUserAsync(
        Guid tenantId, string email, string password, IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        await client.CreateUserAsync(tenantId, new CreateUserRequest
        {
            Email = email,
            Password = password,
            Roles = [.. roles],
        }, cancellationToken).ConfigureAwait(false);
    }
}
