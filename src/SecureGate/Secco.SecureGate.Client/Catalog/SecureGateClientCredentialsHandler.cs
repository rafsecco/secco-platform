using System.Net.Http.Headers;
using System.Text.Json;

namespace Secco.SecureGate.Client.Catalog;

/// <summary>
/// Estado compartilhado de um token de acesso (singleton POR PIPELINE): o pipeline de
/// handlers do <c>IHttpClientFactory</c> é reciclado periodicamente — o cache do token
/// não pode morrer com ele. Cada recurso (catálogo, autorização) tem o próprio store,
/// pois pede um token com o próprio scope (least privilege).
/// </summary>
public sealed class SecureGateAccessTokenStore
{
    /// <summary>Serializa a renovação do token entre requisições concorrentes.</summary>
    internal SemaphoreSlim RefreshLock { get; } = new(1, 1);

    /// <summary>Token vigente; nulo antes da primeira aquisição.</summary>
    internal string? AccessToken { get; set; }

    /// <summary>Expiração do token vigente.</summary>
    internal DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Client credentials do OAuth 2 nos caminhos de plataforma: anexa o Bearer emitido pelo
/// próprio SecureGate com o scope MÍNIMO do recurso (ADR-0020) e renova antes de expirar.
/// O endpoint <c>/connect/token</c> é protocolo OAuth padrão, não contrato de produto —
/// por isso a chamada é HTTP direta e não passa pelo client NSwag (exceção consciente à ADR-0006).
/// </summary>
internal sealed class SecureGateClientCredentialsHandler(
    SecureGateClientCredentialsOptions options,
    SecureGateAccessTokenStore store,
    string scope) : DelegatingHandler
{
    /// <summary>Margem antes da expiração para renovar o token proativamente.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

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
        if (store.AccessToken is { } cached && DateTimeOffset.UtcNow < store.ExpiresAt - ExpiryMargin)
        {
            return cached;
        }

        await store.RefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (store.AccessToken is { } refreshed && DateTimeOffset.UtcNow < store.ExpiresAt - ExpiryMargin)
            {
                return refreshed;
            }

            // base.SendAsync direto: a requisição de token não pode recursar por este handler
            using var tokenRequest = new HttpRequestMessage(
                HttpMethod.Post, new Uri(new Uri(options.BaseUrl!, UriKind.Absolute), "connect/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = options.ClientId!,
                    ["client_secret"] = options.ClientSecret!,
                    ["scope"] = scope,
                }),
            };

            using var response = await base.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // O corpo da resposta não entra na mensagem (ADR-0020)
                throw new HttpRequestException(
                    $"O SecureGate recusou a emissão de token (HTTP {(int)response.StatusCode}).");
            }

            using var payload = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            var accessToken = payload.RootElement.GetProperty("access_token").GetString()
                ?? throw new HttpRequestException("A resposta de token do SecureGate não contém access_token.");

            var expiresIn = payload.RootElement.TryGetProperty("expires_in", out var expiry)
                ? expiry.GetInt32()
                : 3600;

            store.AccessToken = accessToken;
            store.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            return accessToken;
        }
        finally
        {
            store.RefreshLock.Release();
        }
    }
}
