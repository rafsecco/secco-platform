using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Constants;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Monta o <see cref="ClaimsPrincipal"/> emitido no authorization code / refresh (Fase 6.5)
/// com as claims curtas da ADR-0007 (<c>sub</c>, <c>tenant_id</c>, <c>role</c>) e define os
/// destinos por claim: <c>role</c> e <c>tenant_id</c> vão ao access token (o LogStream os lê
/// para tenancy + autorização, ADR-0021), e as claims de perfil só ao id_token quando o
/// scope correspondente foi concedido.
/// </summary>
internal static class OidcPrincipalBuilder
{
    /// <summary>Constrói o principal de um usuário autenticado para os scopes/resources concedidos.</summary>
    /// <param name="user">Usuário autenticado.</param>
    /// <param name="roles">Roles do usuário no seu tenant (ADR-0021).</param>
    /// <param name="scopes">Scopes concedidos na requisição.</param>
    /// <param name="resources">Audiences derivadas dos scopes de produto (o que os produtos validam).</param>
    public static ClaimsPrincipal ForUser(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> scopes,
        IEnumerable<string> resources)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType, Claims.Name, SeccoClaims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(SeccoClaims.TenantId, user.TenantId.ToString());
        identity.SetClaim(Claims.Email, user.Email);
        identity.SetClaim(Claims.Name, user.UserName);
        identity.SetClaims(SeccoClaims.Role, [.. roles]);

        identity.SetScopes(scopes);
        identity.SetResources(resources);
        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Resolve as audiences (resources) dos scopes de produto via scope manager.</summary>
    /// <param name="scopeManager">Gerenciador de scopes do OpenIddict.</param>
    /// <param name="scopes">Scopes concedidos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task<List<string>> ResolveResourcesAsync(
        IOpenIddictScopeManager scopeManager,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken)
    {
        var resources = new List<string>();

        await foreach (var resource in scopeManager.ListResourcesAsync([.. scopes], cancellationToken).ConfigureAwait(false))
        {
            resources.Add(resource);
        }

        return resources;
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // sub e tenant_id: sempre nos dois tokens — o access token precisa do tenant (ADR-0005)
        if (claim.Type is Claims.Subject || claim.Type == SeccoClaims.TenantId)
        {
            return [Destinations.AccessToken, Destinations.IdentityToken];
        }

        // role: sempre no access token (o LogStream resolve permissões dele, ADR-0021);
        // no id_token apenas se o scope 'roles' foi concedido
        if (claim.Type == SeccoClaims.Role)
        {
            return claim.Subject!.HasScope(Scopes.Roles)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [Destinations.AccessToken];
        }

        if (claim.Type is Claims.Email)
        {
            return claim.Subject!.HasScope(Scopes.Email)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [Destinations.AccessToken];
        }

        if (claim.Type is Claims.Name)
        {
            return claim.Subject!.HasScope(Scopes.Profile)
                ? [Destinations.AccessToken, Destinations.IdentityToken]
                : [Destinations.AccessToken];
        }

        return [Destinations.AccessToken];
    }
}
