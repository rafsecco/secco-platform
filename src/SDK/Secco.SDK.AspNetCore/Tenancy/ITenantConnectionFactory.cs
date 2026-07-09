namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Único caminho legítimo para obter a connection string do banco do tenant atual
/// (ADR-0005: proibido connection string fixa de tenant). É a barreira de isolamento:
/// sem tenant resolvido não há acesso a dados, por construção.
/// </summary>
public interface ITenantConnectionFactory
{
    /// <summary>Resolve a connection string do banco dedicado do tenant da requisição atual.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <exception cref="TenantNotResolvedException">Se nenhum tenant foi resolvido para a requisição.</exception>
    /// <exception cref="TenantNotFoundException">Se o tenant resolvido não existe no catálogo.</exception>
    ValueTask<string> GetConnectionStringAsync(CancellationToken cancellationToken = default);
}
