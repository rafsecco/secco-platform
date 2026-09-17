using System.Net;
using System.Text.Json;

namespace Secco.AdminPortal.Authentication;

/// <summary>Renova os tokens do operador no token endpoint do SecureGate.</summary>
public interface IOperatorTokenRefresher
{
	/// <summary>Novo par de tokens, ou <c>null</c> se a renovação foi recusada (sessão revogada).</summary>
	Task<OperatorSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>Grant <c>refresh_token</c> como client confidencial.</summary>
internal sealed class OperatorTokenRefresher(IHttpClientFactory httpClientFactory, IConfiguration configuration)
	: IOperatorTokenRefresher
{
	public async Task<OperatorSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
	{
		var client = httpClientFactory.CreateClient(AdminPortalDefaults.SecureGateHttpClient);
		var authority = configuration["Secco:SecureGate:Authority"]!.TrimEnd('/');

		using var response = await client.PostAsync(authority + "/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken,
			["client_id"] = configuration["Secco:SecureGate:ClientId"]!,
			["client_secret"] = configuration["Secco:SecureGate:ClientSecret"]!,
		}), cancellationToken).ConfigureAwait(false);

		if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
		{
			return null;
		}

		response.EnsureSuccessStatusCode();

		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
		var root = json.RootElement;

		return new OperatorSession(
			root.GetProperty("access_token").GetString()!,
			root.GetProperty("refresh_token").GetString()!,
			DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
	}
}
