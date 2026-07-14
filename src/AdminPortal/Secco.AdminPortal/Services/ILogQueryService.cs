using System.Net.Http.Headers;
using Secco.AdminPortal.Authentication;
using Secco.LogStream.Client;
using Secco.SharedKernel.Constants;

namespace Secco.AdminPortal.Services;

/// <summary>Leitura de logs de um tenant, on-behalf-of o operador (Fase 7.3, ADR-0024).</summary>
public interface ILogQueryService
{
    /// <summary>Busca paginada de log-entries de um tenant.</summary>
    /// <param name="tenantId">Tenant alvo (viaja no header X-Tenant-Id).</param>
    /// <param name="filter">Filtros da busca.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<LogEntryPage> SearchLogEntriesAsync(
        Guid tenantId, LogEntryFilter filter, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consulta o LogStream via <c>Secco.LogStream.Client</c> (ADR-0006), anexando o token do
/// operador e o header <c>X-Tenant-Id</c> do tenant alvo. O operador é tenant-less no token
/// (ADR-0024): o tenant a inspecionar é escolhido por requisição pelo header, e a autorização
/// de leitura vem do read-set cross-tenant resolvido no SecureGate.
/// </summary>
internal sealed class LogStreamQueryService(
    IHttpClientFactory httpClientFactory,
    IOperatorTokenProvider tokenProvider) : ILogQueryService
{
    public async Task<LogEntryPage> SearchLogEntriesAsync(
        Guid tenantId, LogEntryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var http = httpClientFactory.CreateClient(AdminPortalDefaults.LogStreamHttpClient);

        if (await tokenProvider.GetAccessTokenAsync().ConfigureAwait(false) is { Length: > 0 } token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // ADR-0024: o operador escolhe o tenant alvo por requisição (caminho "sem claim → header")
        http.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, tenantId.ToString());

        var client = new LogStreamClient(http);

        var result = await client.LogEntriesGETAsync(
            filter.From, filter.To, ParseLevel(filter.Level), filter.Message,
            correlationId: null, filter.Page, filter.Size, cancellationToken).ConfigureAwait(false);

        return new LogEntryPage(
            [.. result.Items.Select(entry => new LogEntryView(
                entry.Id, entry.Level.ToString(), entry.Message, entry.CreatedAt, entry.CorrelationId))],
            result.Page, result.Size, result.TotalCount, result.TotalPages);
    }

    private static LogEntryLevel? ParseLevel(string? level) =>
        Enum.TryParse<LogEntryLevel>(level, ignoreCase: true, out var parsed) ? parsed : null;
}
