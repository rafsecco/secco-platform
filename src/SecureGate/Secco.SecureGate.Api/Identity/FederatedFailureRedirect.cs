using Microsoft.AspNetCore.WebUtilities;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Para onde a recusa do login federado volta: a tela de login com a mensagem genérica, preservando
/// o <c>returnUrl</c> apenas quando ele é local (sem open redirect, ADR-0020).
/// </summary>
internal static class FederatedFailureRedirect
{
	/// <summary>Destino da recusa sem <c>returnUrl</c>.</summary>
	public const string Path = "/login?federatedError=1";

	/// <summary>Só destino local: não vazio, começa com '/', nem "//" nem "/\".</summary>
	/// <param name="url">Destino candidato.</param>
	public static bool IsLocal(string? url) =>
		!string.IsNullOrEmpty(url)
		&& url.StartsWith('/')
		&& !url.StartsWith("//", StringComparison.Ordinal)
		&& !url.StartsWith("/\\", StringComparison.Ordinal);

	/// <summary>Destino da recusa, com o <c>returnUrl</c> se for local.</summary>
	/// <param name="returnUrl">Retorno pedido pela pessoa.</param>
	public static string For(string? returnUrl) =>
		IsLocal(returnUrl) ? Path + "&returnUrl=" + Uri.EscapeDataString(returnUrl!) : Path;

	/// <summary>Extrai o <c>returnUrl</c> da query do <c>RedirectUri</c> do callback e delega a <see cref="For"/>.</summary>
	/// <param name="redirectUri">RedirectUri das propriedades do desafio (<c>/login/entra-callback?...</c>).</param>
	public static string FromCallbackRedirectUri(string? redirectUri)
	{
		var queryStart = redirectUri?.IndexOf('?') ?? -1;

		if (redirectUri is null || queryStart < 0)
		{
			return Path;
		}

		var query = QueryHelpers.ParseQuery(redirectUri[queryStart..]);

		return For(query.TryGetValue("returnUrl", out var value) ? value.ToString() : null);
	}
}
