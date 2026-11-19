using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Marcas do login federado direto contra laço de redirecionamento. Nenhuma concede acesso: a do logout só
/// impede o authorize de mandar a pessoa de volta ao Entra logo depois de sair, e a da tentativa só avisa que o
/// authorize já foi ao Entra uma vez nesta volta.
/// </summary>
internal static class SignInMarkers
{
	/// <summary>Cookie que registra o instante do logout.</summary>
	public const string SignedOutCookie = "secco.securegate.signed-out";

	/// <summary>Quanto tempo o logout suprime o login direto.</summary>
	public static readonly TimeSpan SignedOutMaxAge = TimeSpan.FromHours(12);

	private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

	/// <summary>Parâmetro de query que marca a volta de uma tentativa de login direto.</summary>
	public const string DirectAttemptParameter = "secco_direct_attempt";

	/// <summary>Grava o cookie do logout (sessão do navegador; a validade real é conferida no valor).</summary>
	/// <param name="context">Requisição.</param>
	/// <param name="now">Instante atual.</param>
	public static void MarkSignedOut(HttpContext context, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(context);

		var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();

		context.Response.Cookies.Append(
			SignedOutCookie,
			now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
			new CookieOptions
			{
				HttpOnly = true,
				SameSite = SameSiteMode.Lax,
				Secure = environment.IsProduction() || context.Request.IsHttps,
				Path = "/",
			});
	}

	/// <summary>Se há logout recente (dentro de <see cref="SignedOutMaxAge"/>); valor inválido conta como ausente.</summary>
	/// <param name="context">Requisição.</param>
	/// <param name="now">Instante atual.</param>
	public static bool HasSignedOut(HttpContext context, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (!context.Request.Cookies.TryGetValue(SignedOutCookie, out var value)
			|| !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
		{
			return false;
		}

		// Valor adiantado além da tolerância de relógio não vale: um cookie "do futuro" suprimiria o login direto
		// durante toda a sessão do navegador
		var age = now.ToUnixTimeSeconds() - seconds;

		return age >= -(long)ClockSkew.TotalSeconds && age <= (long)SignedOutMaxAge.TotalSeconds;
	}

	/// <summary>Apaga o cookie do logout (todo login bem-sucedido).</summary>
	/// <param name="context">Requisição.</param>
	public static void ClearSignedOut(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.Request.Cookies.ContainsKey(SignedOutCookie))
		{
			context.Response.Cookies.Delete(SignedOutCookie, new CookieOptions { Path = "/" });
		}
	}

	/// <summary>Acrescenta a marca de tentativa à URL local, preservando a query original.</summary>
	/// <param name="localUrl">URL local (path e query).</param>
	public static string WithDirectAttempt(string localUrl)
	{
		ArgumentNullException.ThrowIfNull(localUrl);

		// Idempotente: a volta da reautenticação pode passar por aqui com a marca já presente
		var queryStart = localUrl.IndexOf('?', StringComparison.Ordinal);

		if (queryStart >= 0
			&& QueryHelpers.ParseQuery(localUrl[queryStart..]).TryGetValue(DirectAttemptParameter, out var existing)
			&& existing.Any(v => string.Equals(v, "1", StringComparison.Ordinal)))
		{
			return localUrl;
		}

		return QueryHelpers.AddQueryString(localUrl, DirectAttemptParameter, "1");
	}

	/// <summary>Se a requisição traz a marca de tentativa (só o valor <c>1</c> vale).</summary>
	/// <param name="request">Requisição.</param>
	public static bool HasDirectAttempt(HttpRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		return request.Query.TryGetValue(DirectAttemptParameter, out var values)
			&& values.Any(v => string.Equals(v, "1", StringComparison.Ordinal));
	}
}
