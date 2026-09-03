using System.Net.Http.Headers;
using System.Text.Json;

namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>
/// Client credentials do OAuth 2 para chamadas de máquina a máquina dentro da plataforma:
/// anexa o Bearer emitido pelo endpoint de token informado, com o scope MÍNIMO do recurso
/// (ADR-0020), e renova antes de expirar. O endpoint <c>/connect/token</c> é protocolo OAuth
/// padrão, não contrato de produto — por isso a chamada é HTTP direta e não passa por um
/// client NSwag gerado (exceção consciente à ADR-0006). Genérico de propósito: qualquer
/// pacote do SDK que precise de um token de máquina reusa este handler sem depender de
/// nenhum client de produto específico.
/// </summary>
/// <param name="tokenEndpointBaseUrl">URL base do emissor; o handler completa com <c>connect/token</c>.</param>
/// <param name="clientId">Identificador do client OAuth.</param>
/// <param name="clientSecret">Segredo do client OAuth (nunca logado, ADR-0020).</param>
/// <param name="scope">Scope solicitado — o mínimo necessário para o recurso (least privilege).</param>
/// <param name="store">Cache do token vigente, compartilhado entre requisições deste recurso.</param>
public sealed class SeccoClientCredentialsHandler(
	string tokenEndpointBaseUrl,
	string clientId,
	string clientSecret,
	string scope,
	SeccoAccessTokenStore store) : DelegatingHandler
{
	/// <summary>Margem antes da expiração para renovar o token proativamente.</summary>
	private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

	private readonly string _tokenEndpointBaseUrl = ValidateArgument(tokenEndpointBaseUrl, nameof(tokenEndpointBaseUrl));
	private readonly string _clientId = ValidateArgument(clientId, nameof(clientId));
	private readonly string _clientSecret = ValidateArgument(clientSecret, nameof(clientSecret));
	private readonly string _scope = ValidateArgument(scope, nameof(scope));
	private readonly SeccoAccessTokenStore _store = store ?? throw new ArgumentNullException(nameof(store));

	private static string ValidateArgument(string value, string paramName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

		return value;
	}

	/// <inheritdoc />
	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		request.Headers.Authorization = new AuthenticationHeaderValue(
			"Bearer", await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));

		return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}

	private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
	{
		if (_store.AccessToken is { } cached && DateTimeOffset.UtcNow < _store.ExpiresAt - ExpiryMargin)
		{
			return cached;
		}

		await _store.RefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (_store.AccessToken is { } refreshed && DateTimeOffset.UtcNow < _store.ExpiresAt - ExpiryMargin)
			{
				return refreshed;
			}

			// base.SendAsync direto: a requisição de token não pode recursar por este handler
			using var tokenRequest = new HttpRequestMessage(
				HttpMethod.Post, new Uri(new Uri(_tokenEndpointBaseUrl, UriKind.Absolute), "connect/token"))
			{
				Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "client_credentials",
					["client_id"] = _clientId,
					["client_secret"] = _clientSecret,
					["scope"] = _scope,
				}),
			};

			using var response = await base.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				// O corpo da resposta não entra na mensagem (ADR-0020)
				throw new HttpRequestException(
					$"O emissor recusou a emissão de token (HTTP {(int)response.StatusCode}).");
			}

			using var payload = JsonDocument.Parse(
				await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

			var accessToken = payload.RootElement.GetProperty("access_token").GetString()
				?? throw new HttpRequestException("A resposta de token do emissor não contém access_token.");

			var expiresIn = payload.RootElement.TryGetProperty("expires_in", out var expiry)
				? expiry.GetInt32()
				: 3600;

			_store.AccessToken = accessToken;
			_store.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

			return accessToken;
		}
		finally
		{
			_store.RefreshLock.Release();
		}
	}
}
