using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Seed de REFERÊNCIA (ADR-0019): os scopes da plataforma e a estrutura do OPERADOR
/// (ADR-0023). Scopes: um por produto (Fase 6.2, resource = audience validada pelo
/// <c>AddSeccoAuthentication()</c> do produto) e os do catálogo/gestão do SecureGate
/// (Fase 6.3): <c>catalog:&lt;produto&gt;</c> concede leitura do catálogo daquele produto
/// apenas, e <c>securegate:admin</c> a gestão — todos com resource <c>secco-securegate</c>.
/// Estrutura de operador: o tenant de plataforma e o role <c>platform-operator</c> que
/// habilita o scope admin no login (Fase 7.1). Tudo idempotente por nome/id.
/// </summary>
public sealed class SecureGateReferenceDataSeeder(
    IOpenIddictScopeManager scopeManager,
    SecureGateDbContext context) : IReferenceDataSeeder
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

        // Fase 6.4 (ADR-0021): resolução role→permissions — scope único de plataforma
        await UpsertScopeAsync(
            SecureGateScopes.AuthorizationRead,
            "Leitura da resolução role→permissions",
            SecureGateResource,
            cancellationToken).ConfigureAwait(false);

        // Fase 7.1 (ADR-0023): estrutura do operador de plataforma
        await SeedPlatformOperatorAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tenant de plataforma + role <c>platform-operator</c> (gate do scope admin no login).</summary>
    private async Task SeedPlatformOperatorAsync(CancellationToken cancellationToken)
    {
        if (!await context.Tenants.AnyAsync(t => t.Id == SecureGatePlatform.TenantId, cancellationToken).ConfigureAwait(false))
        {
            var tenant = new Tenant(SecureGatePlatform.TenantName, SecureGatePlatform.TenantSlug);
            context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = SecureGatePlatform.TenantId;
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var normalized = SecureGatePlatform.OperatorRole.ToUpperInvariant();

        var roleExists = await context.Roles
            .AnyAsync(r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == normalized, cancellationToken)
            .ConfigureAwait(false);

        if (!roleExists)
        {
            // O role só marca o operador — os poderes vêm do scope admin, não de permissões (ADR-0023)
            context.Roles.Add(new Identity.Role
            {
                Id = Guid.CreateVersion7(),
                TenantId = SecureGatePlatform.TenantId,
                Name = SecureGatePlatform.OperatorRole,
                NormalizedName = normalized,
                ConcurrencyStamp = Guid.NewGuid().ToString(),
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
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
