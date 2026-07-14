using System.Net.Http.Headers;
using Secco.AdminPortal.Authentication;
using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>
/// Cria um <see cref="ISecureGateClient"/> autenticado com o access token do operador
/// (on-behalf-of, ADR-0023). Centraliza o encaminhamento do token — os serviços de gestão
/// só orquestram chamadas e projeção, sem repetir a plumbing de autenticação.
/// </summary>
public interface ISecureGateClientFactory
{
    /// <summary>Constrói um client do SecureGate com o Bearer do operador anexado.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<ISecureGateClient> CreateAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class SecureGateClientFactory(
    IHttpClientFactory httpClientFactory,
    IOperatorTokenProvider tokenProvider) : ISecureGateClientFactory
{
    public async Task<ISecureGateClient> CreateAsync(CancellationToken cancellationToken = default)
    {
        var http = httpClientFactory.CreateClient(AdminPortalDefaults.SecureGateHttpClient);

        if (await tokenProvider.GetAccessTokenAsync().ConfigureAwait(false) is { Length: > 0 } token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return new SecureGateClient(http);
    }
}
