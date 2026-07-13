using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Seed de REFERÊNCIA (ADR-0019): os scopes da plataforma. Um scope por produto (Fase 6.2,
/// resource = audience validada pelo <c>AddSeccoAuthentication()</c> do produto) e os scopes
/// do catálogo/gestão do SecureGate (Fase 6.3): <c>catalog:&lt;produto&gt;</c> concede leitura
/// do catálogo daquele produto apenas, e <c>securegate:admin</c> a gestão — todos com resource
/// <c>secco-securegate</c>, pois a API chamada é o próprio SecureGate. Idempotente por nome.
/// </summary>
public sealed class SecureGateReferenceDataSeeder(IOpenIddictScopeManager scopeManager) : IReferenceDataSeeder
{
    /// <summary>Audience do próprio SecureGate — resource dos scopes de catálogo e gestão.</summary>
    private const string SecureGateResource = "secco-securegate";

    /// <summary>Scopes de produto: nome do scope → audience (resource).</summary>
    private static readonly IReadOnlyDictionary<string, string> ProductScopes = new Dictionary<string, string>
    {
        ["logstream"] = "secco-logstream",
        ["securegate"] = SecureGateResource,
    };

    /// <summary>Produtos multi-tenant com catálogo servido pelo SecureGate (ADR-0005).</summary>
    private static readonly IReadOnlyList<string> CatalogProducts = ["logstream"];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (scopeName, resource) in ProductScopes)
        {
            await UpsertScopeAsync(scopeName, $"Acesso ao produto {resource}", resource, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var product in CatalogProducts)
        {
            await UpsertScopeAsync(
                SecureGateScopes.CatalogFor(product),
                $"Leitura do catálogo de tenants do produto {product}",
                SecureGateResource,
                cancellationToken).ConfigureAwait(false);
        }

        await UpsertScopeAsync(
            SecureGateScopes.Admin,
            "Gestão do SecureGate (tenants e bancos)",
            SecureGateResource,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertScopeAsync(
        string scopeName,
        string displayName,
        string resource,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = scopeName,
            DisplayName = displayName,
            Resources = { resource },
        };

        var existing = await scopeManager.FindByNameAsync(scopeName, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            await scopeManager.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await scopeManager.UpdateAsync(existing, descriptor, cancellationToken).ConfigureAwait(false);
        }
    }
}
