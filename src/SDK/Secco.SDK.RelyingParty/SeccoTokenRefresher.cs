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
		var form = new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = current.RefreshToken,
		};

		using var response = await SeccoTokenEndpoint.PostAsync(oidcOptions, options.Value, form, cancellationToken)
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

		// Do token novo, nunca do anterior: escopo retirado na renovação some daqui (ADR-0044)
		var scopes = SeccoUserSession.ScopesOf(
			accessToken, root.TryGetProperty("scope", out var scope) ? scope.GetString() : null);

		// O instante e os métodos de autenticação também vêm do token novo (ADR-0050); os tokens trocados
		// são preservados pelo provedor, que é quem grava a sessão sob a trava
		return new SeccoUserSession(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn), scopes)
			.WithAuthenticationOf(accessToken);
	}
}
