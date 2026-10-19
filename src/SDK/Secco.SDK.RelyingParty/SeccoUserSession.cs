namespace Secco.SDK.RelyingParty;

/// <summary>
/// Tokens da pessoa guardados no servidor — nunca no cookie (ADR-0032, ADR-0046). O <c>ToString</c> do
/// record é suprimido para os tokens não vazarem por log ou mensagem de exceção (ADR-0020).
/// </summary>
/// <param name="AccessToken">Access token atual.</param>
/// <param name="RefreshToken">Refresh token atual (rotativo).</param>
/// <param name="ExpiresAt">Vencimento do access token.</param>
/// <param name="Scopes">Escopos concedidos ao token atual.</param>
public sealed record SeccoUserSession(
	string AccessToken,
	string RefreshToken,
	DateTimeOffset ExpiresAt,
	IReadOnlyList<string> Scopes)
{
	/// <inheritdoc />
	public override string ToString() => nameof(SeccoUserSession);

	/// <summary>Separa a lista de escopos do OAuth 2 (separados por espaço).</summary>
	/// <param name="scope">Valor do parâmetro <c>scope</c>; <c>null</c> ou vazio dá lista vazia.</param>
	internal static IReadOnlyList<string> ParseScopes(string? scope) =>
		string.IsNullOrWhiteSpace(scope)
			? []
			: scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
