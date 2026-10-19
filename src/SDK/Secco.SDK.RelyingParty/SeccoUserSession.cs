using Microsoft.IdentityModel.JsonWebTokens;

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

	/// <summary>
	/// Escopos do access token: a claim <c>scope</c> do próprio token quando ele é um JWT legível, e só senão o
	/// parâmetro <c>scope</c> da resposta. Nunca os escopos do token anterior — uma concessão revogada (por
	/// exemplo, <c>securegate:tenant-admin</c>, ADR-0044) tem de sumir na renovação, não sobreviver nela.
	/// </summary>
	/// <param name="accessToken">Access token recém-emitido.</param>
	/// <param name="responseScope">Parâmetro <c>scope</c> da resposta do token endpoint, se houver.</param>
	internal static IReadOnlyList<string> ScopesOf(string accessToken, string? responseScope)
	{
		var handler = new JsonWebTokenHandler();

		// Só leitura do payload, sem validar: o token acabou de chegar do token endpoint pelo backchannel, e a
		// validação de verdade é de quem o recebe (ADR-0007). Aqui ele só decide o que a tela mostra.
		if (handler.CanReadToken(accessToken))
		{
			var claims = handler.ReadJsonWebToken(accessToken).Claims
				.Where(claim => claim.Type == "scope")
				.Select(claim => claim.Value)
				.ToList();

			if (claims.Count > 0)
			{
				return [.. claims.SelectMany(ParseScopes).Distinct(StringComparer.Ordinal)];
			}
		}

		return ParseScopes(responseScope);
	}

	/// <summary>Separa a lista de escopos do OAuth 2 (separados por espaço).</summary>
	/// <param name="scope">Valor do parâmetro <c>scope</c>; <c>null</c> ou vazio dá lista vazia.</param>
	internal static IReadOnlyList<string> ParseScopes(string? scope) =>
		string.IsNullOrWhiteSpace(scope)
			? []
			: scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
