using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>
/// Cache obrigatório de permissões da plataforma (ADR-0021): chave <c>(tenant_id, role)</c>,
/// TTL curto configurável (<c>Secco:Authorization:CacheTtlSeconds</c>). <b>Estrito e
/// fail-closed</b> — diferente do catálogo de tenants (stale em falha), aqui entrada
/// expirada + resolução falhando propaga a exceção e o acesso é NEGADO: autorização
/// nunca falha aberta, e o TTL é o teto real da janela de revogação.
/// </summary>
internal sealed class CachedPermissionResolver(
    IPermissionResolver inner,
    IOptions<SeccoAuthorizationOptions> options)
{
    private sealed record Entry(IReadOnlySet<string> Permissions, DateTimeOffset FreshUntil);

    private readonly ConcurrentDictionary<(Guid TenantId, string Role), Entry> _entries = new();

    /// <summary>Resolve com cache; propaga falhas do resolver (o handler nega — fail-closed).</summary>
    /// <param name="tenantId">Tenant do contexto da requisição.</param>
    /// <param name="role">Nome do role.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async ValueTask<IReadOnlySet<string>> ResolveAsync(
        Guid tenantId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var key = (tenantId, role);

        if (_entries.TryGetValue(key, out var cached) && now < cached.FreshUntil)
        {
            return cached.Permissions;
        }

        var permissions = await inner.ResolveAsync(tenantId, role, cancellationToken).ConfigureAwait(false);

        var ttlSeconds = options.Value.CacheTtlSeconds;
        _entries[key] = new Entry(permissions, now + TimeSpan.FromSeconds(ttlSeconds > 0 ? ttlSeconds : 60));

        return permissions;
    }
}
