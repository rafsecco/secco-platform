using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Seed de REFERÊNCIA (ADR-0019): os scopes de produto da plataforma — um por produto
/// (decisão da fase 6.2), com o resource = audience que o <c>AddSeccoAuthentication()</c>
/// de cada produto valida. Idempotente por nome; roda em todos os ambientes.
/// </summary>
public sealed class SecureGateReferenceDataSeeder(IOpenIddictScopeManager scopeManager) : IReferenceDataSeeder
{
    /// <summary>Scopes de produto: nome do scope → audience (resource).</summary>
    private static readonly IReadOnlyDictionary<string, string> ProductScopes = new Dictionary<string, string>
    {
        ["logstream"] = "secco-logstream",
        ["securegate"] = "secco-securegate",
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (scopeName, resource) in ProductScopes)
        {
            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = scopeName,
                DisplayName = $"Acesso ao produto {resource}",
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
}
