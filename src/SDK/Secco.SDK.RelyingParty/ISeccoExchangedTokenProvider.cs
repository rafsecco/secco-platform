using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// Token curto obtido trocando o da pessoa por um escopo (RFC 8693, ADR-0031, ADR-0049, ADR-0050) — por exemplo
/// <c>logstream</c> (elevação) ou <c>securegate:admin</c> (operador designado). A troca só acontece quando a
/// aplicação pede: nunca é renovada em segundo plano, porque poder elevado não nasce sem uma ação da pessoa.
/// </summary>
public interface ISeccoExchangedTokenProvider
{
	/// <summary>
	/// Token trocado para o escopo: o do cofre da sessão enquanto válido, senão uma nova troca no token endpoint
	/// com o access token atual da pessoa como <c>subject_token</c>. A recusa do emissor (<c>400</c>/<c>401</c>)
	/// vira <see cref="SeccoExchangeStatus.Refused"/> sem exceção, e é genérica de propósito (a plataforma não
	/// diz o motivo): quem decide pedir reautenticação é a aplicação. Falha de infraestrutura (5xx, rede) lança.
	/// </summary>
	/// <param name="scope">Um único escopo (sem espaços).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="ArgumentException">O escopo é vazio, tem espaço ou caractere inválido.</exception>
	Task<SeccoExchangeResult> GetAsync(string scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// A troca grava no cofre sob a mesma trava por sessão da renovação, relendo a sessão dentro da trava: sem isso,
/// uma renovação concorrente ou outra troca seria sobrescrita (atualização perdida). Nenhum token vai a log.
/// </summary>
internal sealed class SeccoExchangedTokenProvider(
	ISeccoSessionResolver resolver,
	ISeccoUserSessionStore store,
	IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
	IOptions<SeccoRelyingPartyOptions> options) : ISeccoExchangedTokenProvider
{
	private const int DefaultExpiresInSeconds = 60;

	private const int MaxScopeLength = 256;

	private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";

	private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

	public async Task<SeccoExchangeResult> GetAsync(string scope, CancellationToken cancellationToken = default)
	{
		ValidateScope(scope);

		// Renova o token da pessoa se preciso (fora da trava desta troca: a renovação tem a dela)
		if (await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false) is not { } resolved)
		{
			return SeccoExchangeResult.NoSession();
		}

		if (Cached(resolved.Session, scope) is { } hit)
		{
			return hit;
		}

		var gate = SeccoSessionLocks.For(resolved.SessionId);
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// Relê sob a trava: outra chamada pode ter trocado ou renovado enquanto esta esperava
			var session = await store.GetAsync(resolved.SessionId, cancellationToken).ConfigureAwait(false);

			if (session is null)
			{
				return SeccoExchangeResult.NoSession();
			}

			if (Cached(session, scope) is { } concurrent)
			{
				return concurrent;
			}

			var exchanged = await ExchangeAsync(session.AccessToken, scope, cancellationToken).ConfigureAwait(false);

			if (exchanged is null)
			{
				return SeccoExchangeResult.Refused();
			}

			// Poda os vencidos para o cofre não acumular token morto
			var now = DateTimeOffset.UtcNow;
			var tokens = session.ExchangedTokens
				.Where(pair => pair.Value.ExpiresAt > now && pair.Key != scope)
				.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
			tokens[scope] = exchanged;

			await store.SetAsync(resolved.SessionId, session with { ExchangedTokens = tokens }, cancellationToken).ConfigureAwait(false);

			return SeccoExchangeResult.Issued(exchanged.AccessToken, exchanged.ExpiresAt);
		}
		finally
		{
			gate.Release();
		}
	}

	private static void ValidateScope(string scope)
	{
		ArgumentException.ThrowIfNullOrEmpty(scope);

		// scope-token do RFC 6749 §3.3: ASCII imprimível sem espaço, aspas e barra invertida
		if (scope.Length > MaxScopeLength || scope.Any(c => c is < '!' or > '~' or '"' or '\\'))
		{
			throw new ArgumentException("O escopo deve ser um único token (sem espaços ou caracteres inválidos).", nameof(scope));
		}
	}

	private SeccoExchangeResult? Cached(SeccoUserSession session, string scope) =>
		session.ExchangedTokens.TryGetValue(scope, out var token)
		&& token.ExpiresAt - DateTimeOffset.UtcNow > options.Value.RefreshMargin
			? SeccoExchangeResult.Issued(token.AccessToken, token.ExpiresAt)
			: null;

	private async Task<SeccoExchangedToken?> ExchangeAsync(string subjectToken, string scope, CancellationToken cancellationToken)
	{
		var form = new Dictionary<string, string>
		{
			["grant_type"] = TokenExchangeGrant,
			["subject_token"] = subjectToken,
			["subject_token_type"] = AccessTokenType,
			["requested_token_type"] = AccessTokenType,
			["scope"] = scope,
		};

		using var response = await SeccoTokenEndpoint.PostAsync(oidcOptions, options.Value, form, cancellationToken)
			.ConfigureAwait(false);

		// Recusa genérica do emissor: o motivo não é exposto nem lido (ADR-0031)
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
			throw new InvalidOperationException("O token endpoint não devolveu access_token na troca.");
		}

		var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
			? seconds
			: DefaultExpiresInSeconds;

		return new SeccoExchangedToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
	}
}
