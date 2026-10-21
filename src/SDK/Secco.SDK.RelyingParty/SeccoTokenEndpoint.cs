using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// POST ao token endpoint do emissor como o client OIDC da aplicação: endpoint vindo do discovery, client id/secret
/// e <c>Backchannel</c> do próprio <see cref="OpenIdConnectOptions"/> — nada de configuração duplicada.
/// </summary>
internal static class SeccoTokenEndpoint
{
	public static async Task<HttpResponseMessage> PostAsync(
		IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
		SeccoRelyingPartyOptions options,
		Dictionary<string, string> form,
		CancellationToken cancellationToken)
	{
		var oidc = oidcOptions.Get(options.OpenIdConnectScheme);
		var configuration = oidc.Configuration
			?? await (oidc.ConfigurationManager ?? throw new InvalidOperationException(
				"O esquema OpenID Connect não tem ConfigurationManager; configure Authority ou MetadataAddress."))
				.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

		if (!string.IsNullOrEmpty(oidc.ClientId))
		{
			form["client_id"] = oidc.ClientId;
		}

		if (!string.IsNullOrEmpty(oidc.ClientSecret))
		{
			form["client_secret"] = oidc.ClientSecret;
		}

		return await oidc.Backchannel.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken)
			.ConfigureAwait(false);
	}
}
