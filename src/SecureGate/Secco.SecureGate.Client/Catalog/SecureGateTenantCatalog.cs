using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.SecureGate.Client.Catalog;

/// <summary>
/// <see cref="ITenantCatalog"/> sobre a API de catálogo do SecureGate (Fase 6.3, ADR-0005):
/// substitui o catálogo por configuração fora de DEV. Cache in-memory por tenant com TTL
/// curto e <b>stale em falha</b>: SecureGate indisponível não derruba produtos que já
/// resolveram o tenant — entrada expirada é servida com warning. Tenant nunca visto com o
/// SecureGate fora do ar vira <see cref="TenantCatalogUnavailableException"/> (503 +
/// Retry-After pelo pipeline de tenancy do SDK). Connection strings jamais são logadas.
/// </summary>
public sealed class SecureGateTenantCatalog(
    IHttpClientFactory httpClientFactory,
    SecureGateTenantCatalogOptions options,
    ILogger<SecureGateTenantCatalog> logger) : ITenantCatalog
{
    /// <summary>Nome do <c>HttpClient</c> nomeado usado pelo catálogo remoto.</summary>
    public const string HttpClientName = "Secco.SecureGate.TenantCatalog";

    private sealed record Entry(TenantInfo? Value, DateTimeOffset FreshUntil);

    private sealed record ListEntry(IReadOnlyList<TenantInfo> Values, DateTimeOffset FreshUntil);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private volatile ListEntry? _list;

    private TimeSpan Ttl => TimeSpan.FromSeconds(options.CacheTtlSeconds);

    /// <inheritdoc />
    public async ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (_entries.TryGetValue(tenantId, out var cached) && now < cached.FreshUntil)
        {
            return cached.Value;
        }

        try
        {
            var dto = await CreateClient()
                .GetCatalogTenantAsync(options.NormalizedProduct, tenantId, cancellationToken)
                .ConfigureAwait(false);

            var info = new TenantInfo(dto.TenantId, dto.ConnectionString);
            _entries[tenantId] = new Entry(info, now + Ttl);

            return info;
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.NotFound)
        {
            // Negativo também é cacheado: tenant desconhecido não martela o SecureGate
            _entries[tenantId] = new Entry(null, now + Ttl);

            return null;
        }
        catch (Exception exception) when (IsUnavailability(exception, cancellationToken))
        {
            if (cached is not null)
            {
                CatalogLog.ServingStaleEntry(logger, tenantId, exception);

                return cached.Value;
            }

            throw new TenantCatalogUnavailableException(
                "O catálogo de tenants do SecureGate está indisponível e não há entrada em cache para o tenant.",
                exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cached = _list;

        if (cached is not null && now < cached.FreshUntil)
        {
            return cached.Values;
        }

        try
        {
            var dtos = await CreateClient()
                .ListCatalogTenantsAsync(options.NormalizedProduct, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<TenantInfo> values =
                [.. dtos.Select(dto => new TenantInfo(dto.TenantId, dto.ConnectionString))];

            _list = new ListEntry(values, now + Ttl);

            // A listagem também renova as entradas individuais — menos chamadas no caminho quente
            foreach (var value in values)
            {
                _entries[value.TenantId] = new Entry(value, now + Ttl);
            }

            return values;
        }
        catch (Exception exception) when (IsUnavailability(exception, cancellationToken))
        {
            if (cached is not null)
            {
                CatalogLog.ServingStaleList(logger, exception);

                return cached.Values;
            }

            throw new TenantCatalogUnavailableException(
                "O catálogo de tenants do SecureGate está indisponível e não há listagem em cache.",
                exception);
        }
    }

    private SecureGateClient CreateClient() =>
        new(httpClientFactory.CreateClient(HttpClientName));

    /// <summary>
    /// Falhas transitórias que autorizam stale: rede/timeout e QUALQUER status inesperado da
    /// API (5xx, 401 por config quebrada...). Cancelamento do chamador nunca vira stale.
    /// </summary>
    private static bool IsUnavailability(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is HttpRequestException or ApiException or TaskCanceledException or OperationCanceledException;

    private static class StatusCodes
    {
        public const int NotFound = 404;
    }
}

/// <summary>Mensagens de log estruturadas do catálogo remoto (source generator — ADR-0008).</summary>
internal static partial class CatalogLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Catálogo do SecureGate indisponível — servindo entrada expirada do cache para o tenant {TenantId}.")]
    public static partial void ServingStaleEntry(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Catálogo do SecureGate indisponível — servindo listagem expirada do cache.")]
    public static partial void ServingStaleList(ILogger logger, Exception exception);
}
