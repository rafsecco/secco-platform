namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Fornece o access token do operador para as chamadas on-behalf-of às APIs de produto
/// (ADR-0023). O token é custodiado no principal do cookie de sessão (claim
/// <see cref="AdminPortalDefaults.AccessTokenClaim"/>) — nunca no browser.
/// </summary>
public interface IOperatorTokenProvider
{
    /// <summary>Retorna o access token do operador autenticado, ou <c>null</c> se ausente.</summary>
    Task<string?> GetAccessTokenAsync();
}
