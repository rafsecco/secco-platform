using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Secco.SecureGate.Application;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Instante da autenticação no cookie de login (ADR-0049). Só as telas que de fato autenticam a pessoa — senha
/// sem segundo fator, segundo fator, login federado — marcam a requisição como autenticação NOVA; qualquer outra
/// gravação do cookie (renovação depois de trocar a senha ou o segundo fator) copia o instante do cookie anterior.
/// Sem isto o <c>auth_time</c> andaria junto com a expiração deslizante, e "autenticou há pouco" não diria nada.
/// </summary>
internal static class FreshAuthentication
{
	private const string ItemKey = "secco:fresh-authentication";

	/// <summary>Marca a requisição: o próximo login no cookie é uma autenticação nova.</summary>
	/// <param name="context">Requisição da tela de login.</param>
	public static void Mark(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		context.Items[ItemKey] = true;
	}

	/// <summary>Evento <c>OnSigningIn</c> do cookie de login: grava ou preserva o instante da autenticação.</summary>
	/// <param name="context">Contexto da gravação do cookie.</param>
	public static async Task OnSigningInAsync(CookieSigningInContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.Principal?.Identity is not ClaimsIdentity identity
			|| identity.HasClaim(claim => claim.Type == SecureGatePlatform.AuthenticationTimeCookieClaim))
		{
			return;
		}

		var value = context.HttpContext.Items.ContainsKey(ItemKey)
			? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
			: (await context.HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false))
				.Principal?.FindFirst(SecureGatePlatform.AuthenticationTimeCookieClaim)?.Value;

		// Sem instante conhecido nada é gravado: ausente vale como "não autenticou há pouco" (fail-closed)
		if (value is not null)
		{
			identity.AddClaim(new Claim(SecureGatePlatform.AuthenticationTimeCookieClaim, value));
		}
	}

	/// <summary>Instante da autenticação gravado no cookie, se houver.</summary>
	/// <param name="principal">Principal do cookie.</param>
	public static DateTimeOffset? AuthenticatedAt(ClaimsPrincipal? principal) =>
		long.TryParse(
			principal?.FindFirst(SecureGatePlatform.AuthenticationTimeCookieClaim)?.Value,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var seconds)
			? DateTimeOffset.FromUnixTimeSeconds(seconds)
			: null;

	/// <summary>
	/// Métodos de autenticação do cookie (<c>amr</c>): o ASP.NET Identity grava <c>mfa</c> quando o login passou
	/// pelo segundo fator.
	/// </summary>
	/// <param name="principal">Principal do cookie.</param>
	public static IReadOnlyList<string> MethodsOf(ClaimsPrincipal? principal) =>
		[.. principal?.FindAll("amr").Select(claim => claim.Value).Distinct(StringComparer.Ordinal) ?? []];
}
