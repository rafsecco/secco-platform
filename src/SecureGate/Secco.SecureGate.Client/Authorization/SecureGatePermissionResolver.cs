using Secco.SDK.AspNetCore.Authorization;

namespace Secco.SecureGate.Client.Authorization;

/// <summary>
/// <see cref="IPermissionResolver"/> sobre a API de autorização do SecureGate (Fase 6.4,
/// ADR-0021): resolve <c>(tenant, role) → permissões</c> com token client credentials de
/// scope <c>authorization:read</c> (anexado pelo handler do pipeline nomeado). SEM cache
/// próprio — o cache obrigatório da plataforma (TTL curto, fail-closed) fica no SDK,
/// envolvendo qualquer resolver. Falhas propagam: o handler de autorização NEGA o acesso
/// (autorização nunca falha aberta).
/// </summary>
public sealed class SecureGatePermissionResolver(IHttpClientFactory httpClientFactory) : IPermissionResolver
{
    /// <summary>Nome do <c>HttpClient</c> nomeado usado pela resolução de permissões.</summary>
    public const string HttpClientName = "Secco.SecureGate.Authorization";

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> ResolveAsync(
        Guid tenantId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var client = new SecureGateClient(httpClientFactory.CreateClient(HttpClientName));

        var permissions = await client.GetRolePermissionsAsync(tenantId, role, cancellationToken)
            .ConfigureAwait(false);

        return permissions.ToHashSet(StringComparer.Ordinal);
    }
}
