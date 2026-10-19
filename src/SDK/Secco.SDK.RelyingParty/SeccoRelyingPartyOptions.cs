using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Secco.SDK.RelyingParty;

/// <summary>Opções da custódia do token da pessoa (ADR-0046).</summary>
public sealed class SeccoRelyingPartyOptions
{
	/// <summary>Claim do cookie que leva o id da sessão no cofre — o token nunca vai ao cookie (ADR-0032).</summary>
	public string SessionIdClaim { get; set; } = "secco_session";

	/// <summary>Esquema do cookie de sessão da aplicação.</summary>
	public string CookieScheme { get; set; } = CookieAuthenticationDefaults.AuthenticationScheme;

	/// <summary>Esquema OpenID Connect da aplicação (de onde saem authority, client e token endpoint).</summary>
	public string OpenIdConnectScheme { get; set; } = OpenIdConnectDefaults.AuthenticationScheme;

	/// <summary>Validade deslizante da sessão no cofre.</summary>
	public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

	/// <summary>Antecedência com que o access token é renovado antes de vencer.</summary>
	public TimeSpan RefreshMargin { get; set; } = TimeSpan.FromSeconds(60);
}
