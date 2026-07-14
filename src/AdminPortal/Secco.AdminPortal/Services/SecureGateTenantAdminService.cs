using System.Net.Http.Headers;
using Secco.AdminPortal.Authentication;
using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>
/// Gestão de tenants via <see cref="SecureGateClient"/> (ADR-0006), anexando o access token
/// do operador a cada chamada (on-behalf-of, ADR-0023). O <c>HttpClient</c> nomeado herda o
/// pipeline de resiliência da plataforma (<c>AddSeccoResilience</c>).
/// </summary>
internal sealed class SecureGateTenantAdminService(
    IHttpClientFactory httpClientFactory,
    IOperatorTokenProvider tokenProvider) : ITenantAdminService
{
    public async Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        var http = httpClientFactory.CreateClient(AdminPortalDefaults.SecureGateHttpClient);

        if (await tokenProvider.GetAccessTokenAsync().ConfigureAwait(false) is { Length: > 0 } token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var client = new SecureGateClient(http);
        var tenants = await client.ListTenantsAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. tenants.Select(tenant => new TenantSummary(
                tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt))
        ];
    }
}
