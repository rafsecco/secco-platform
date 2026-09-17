namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Fornece o access token do operador para as chamadas on-behalf-of às APIs de produto
/// (ADR-0023). O token vive num cofre no servidor (ADR-0032); o cookie leva apenas o id da
/// sessão (claim <see cref="AdminPortalDefaults.SessionIdClaim"/>) — nunca o token.
/// </summary>
public interface IOperatorTokenProvider
{
	/// <summary>
	/// Access token válido do operador, renovando perto do vencimento; <c>null</c> quando a sessão não
	/// existe mais e a pessoa foi mandada para um novo login.
	/// </summary>
	Task<string?> GetAccessTokenAsync();
}
