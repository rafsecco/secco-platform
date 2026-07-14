using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Lista os usuários de um tenant com seus roles (visão de gestão).</summary>
public sealed class ListUsersHandler(IUserDirectory userDirectory)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="tenantId">Tenant dono dos usuários.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<IReadOnlyList<UserDto>>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var users = await userDirectory.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return Result.Success(users);
    }
}
