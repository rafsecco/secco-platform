using Secco.SDK.AspNetCore.Authentication;
using Secco.SecureGate.Client.Catalog;

namespace Secco.SecureGate.Client.Authorization;

/// <summary>Versão de sessão consultada no SecureGate (ADR-0032), via client credentials com <c>authorization:read</c>.</summary>
public sealed class SecureGateSessionVersionResolver(
	IHttpClientFactory httpClientFactory,
	SecureGateClientCredentialsOptions options) : ISessionVersionResolver
{
	/// <summary>Nome do <see cref="HttpClient"/> nomeado.</summary>
	public const string HttpClientName = "Secco.SecureGate.SessionVersion";

	/// <inheritdoc />
	public bool IsEnabled => options.IsConfigured;

	/// <inheritdoc />
	public async ValueTask<SessionVersionStatus> ResolveAsync(string subject, CancellationToken cancellationToken = default)
	{
		var client = new SecureGateClient(httpClientFactory.CreateClient(HttpClientName));
		var result = await client.GetSessionVersionAsync(subject, cancellationToken).ConfigureAwait(false);

		return new SessionVersionStatus(result.SessionVersion, result.Revoked);
	}
}
