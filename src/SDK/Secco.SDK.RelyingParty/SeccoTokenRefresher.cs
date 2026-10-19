using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Secco.SDK.RelyingParty;

/// <summary>Renova os tokens da pessoa no token endpoint do emissor (grant <c>refresh_token</c>).</summary>
internal interface ISeccoTokenRefresher
{
	/// <summary>Novo par de tokens, ou <c>null</c> se a renovação foi recusada (sessão revogada, ADR-0032).</summary>
	Task<SeccoUserSession?> RefreshAsync(SeccoUserSession current, CancellationToken cancellationToken = default);
}

/// <summary>
/// Grant <c>refresh_token</c> como o client OIDC da aplicação: endpoint vindo do discovery, client id/secret e
/// <c>Backchannel</c> do próprio <see cref="OpenIdConnectOptions"/> — nada de configuração duplicada.
/// </summary>
internal sealed class SeccoTokenRefresher(
	IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
	IOptions<SeccoRelyingPartyOptions> options) : ISeccoTokenRefresher
{
	private const int DefaultExpiresInSeconds = 300;

	public async Task<SeccoUserSession?> RefreshAsync(SeccoUserSession current, CancellationToken cancellationToken = default)
	{
		var oidc = oidcOptions.Get(options.Value.OpenIdConnectScheme);
		var configuration = oidc.Configuration
			?? await (oidc.ConfigurationManager ?? throw new InvalidOperationException(
				"O esquema OpenID Connect não tem ConfigurationManager; configure Authority ou MetadataAddress."))
				.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

		var form = new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = current.RefreshToken,
		};

		if (!string.IsNullOrEmpty(oidc.ClientId))
		{
			form["client_id"] = oidc.ClientId;
		}

		if (!string.IsNullOrEmpty(oidc.ClientSecret))
		{
			form["client_secret"] = oidc.ClientSecret;
		}

		using var response = await oidc.Backchannel.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken)
			.ConfigureAwait(false);

		// Recusa do emissor (invalid_grant: sessão revogada, refresh já usado): a pessoa precisa de novo login
		if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
		{
			return null;
		}

		response.EnsureSuccessStatusCode();

		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
		var root = json.RootElement;

		var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;

		if (string.IsNullOrEmpty(accessToken))
		{
			throw new InvalidOperationException("O token endpoint não devolveu access_token na renovação.");
		}

		// Refresh rotativo: o novo substitui o anterior; emissor que não rotaciona mantém o atual
		var refreshToken = root.TryGetProperty("refresh_token", out var refresh) && refresh.GetString() is { Length: > 0 } rotated
			? rotated
			: current.RefreshToken;

		var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
			? seconds
			: DefaultExpiresInSeconds;

		var scopes = root.TryGetProperty("scope", out var scope) && scope.GetString() is { Length: > 0 } granted
			? SeccoUserSession.ParseScopes(granted)
			: current.Scopes;

		return new SeccoUserSession(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn), scopes);
	}
}
