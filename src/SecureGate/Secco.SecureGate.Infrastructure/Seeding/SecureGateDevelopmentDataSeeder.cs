using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Seed de DESENVOLVIMENTO (ADR-0019, guarda dupla): um tenant demo (o mesmo Guid dos
/// appsettings de DEV dos produtos), um client de console (client credentials), um client
/// web (authorization code + PKCE, Fase 6.5) e um usuário demo com senha conhecida —
/// jamais chega a produção (a orquestração do SDK garante).
/// </summary>
public sealed class SecureGateDevelopmentDataSeeder(
    SecureGateDbContext context,
    IOpenIddictApplicationManager applicationManager,
    UserManager<Identity.User> userManager) : IDevelopmentDataSeeder
{
    /// <summary>Client web de desenvolvimento (authorization code + PKCE, público — sem secret).</summary>
    public const string DevWebClientId = "secco-dev-webapp";

    /// <summary>Usuário demo do tenant de desenvolvimento.</summary>
    public const string DevUserEmail = "dev@secco.local";

    /// <summary>Senha do usuário demo (conhecida — só existe em DEV; satisfaz a política do Identity).</summary>
    public const string DevUserPassword = "Dev@Secco2026";

    /// <summary>Client confidencial do AdminPortal (authorization code + PKCE, Fase 7.1).</summary>
    public const string AdminPortalClientId = "secco-adminportal";

    /// <summary>Secret do client do AdminPortal (conhecido — só existe em DEV).</summary>
    public const string AdminPortalClientSecret = "secco-adminportal-secret-32-chars-min!";

    /// <summary>Usuário OPERADOR de plataforma (ADR-0023) — recebe o scope admin no login.</summary>
    public const string OperatorEmail = "operador@secco.local";

    /// <summary>Senha do operador demo (conhecida — só existe em DEV).</summary>
    public const string OperatorPassword = "Op3rador@Secco!";
    /// <summary>Tenant demo — mesmo Guid usado nos appsettings.Development dos produtos.</summary>
    public static readonly Guid DemoTenantId = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    /// <summary>Client de desenvolvimento para chamadas de console/testes manuais.</summary>
    public const string DevClientId = "secco-dev-console";

    /// <summary>Secret do client de desenvolvimento (conhecido — só existe em DEV).</summary>
    public const string DevClientSecret = "secco-dev-console-secret-32-chars-min!";

    /// <summary>Role demo com as permissões do LogStream (Fase 6.4) — atribuído ao client de DEV.</summary>
    public const string DevRoleName = "dev-admin";

    /// <summary>Permissões do role demo (strings literais: as constantes vivem em cada produto, ADR-0003).</summary>
    private static readonly string[] DevRolePermissions =
    [
        "log-entries:read", "log-entries:write",
        "log-processes:read", "log-processes:write",
        "api-call-logs:read", "api-call-logs:write",
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await context.Tenants.AnyAsync(t => t.Id == DemoTenantId, cancellationToken).ConfigureAwait(false))
        {
            var tenant = new Tenant("Tenant de Desenvolvimento", "dev-alfa");
            context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = DemoTenantId;
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await SeedDevRoleAsync(cancellationToken).ConfigureAwait(false);

        if (await applicationManager.FindByClientIdAsync(DevClientId, cancellationToken).ConfigureAwait(false) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = DevClientId,
                ClientSecret = DevClientSecret,
                DisplayName = "Console de desenvolvimento Secco",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + "logstream",
                    Permissions.Prefixes.Scope + "securegate",
                    // Fase 6.3 — console de DEV também exercita catálogo e gestão localmente
                    Permissions.Prefixes.Scope + Application.SecureGateScopes.CatalogFor("logstream"),
                    Permissions.Prefixes.Scope + Application.SecureGateScopes.Admin,
                    // Fase 6.4 — e a resolução role→permissions
                    Permissions.Prefixes.Scope + Application.SecureGateScopes.AuthorizationRead,
                },
            }, cancellationToken).ConfigureAwait(false);
        }

        // Fase 6.4 (ADR-0021): o console de DEV carrega o role demo na claim curta 'role'
        if (await applicationManager.FindByClientIdAsync(DevClientId, cancellationToken).ConfigureAwait(false)
            is OpenIddict.OidcApplication { Roles: null or "" } devClient)
        {
            devClient.Roles = DevRoleName;
            await applicationManager.UpdateAsync(devClient, cancellationToken).ConfigureAwait(false);
        }

        await SeedWebClientAsync(cancellationToken).ConfigureAwait(false);
        await SeedDevUserAsync(cancellationToken).ConfigureAwait(false);

        // Fase 7.1 (ADR-0023): client do AdminPortal + usuário operador de plataforma
        await SeedAdminPortalClientAsync(cancellationToken).ConfigureAwait(false);
        await SeedOperatorUserAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Client confidencial do AdminPortal: authorization code + PKCE + refresh, consent implícito.</summary>
    private async Task SeedAdminPortalClientAsync(CancellationToken cancellationToken)
    {
        if (await applicationManager.FindByClientIdAsync(AdminPortalClientId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = AdminPortalClientId,
            ClientSecret = AdminPortalClientSecret,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Secco AdminPortal",
            RedirectUris = { new Uri("https://localhost:7180/signin-oidc") },
            PostLogoutRedirectUris = { new Uri("https://localhost:7180/signout-callback-oidc") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                // O scope admin é PERMITIDO ao client, mas só é EMITIDO a operadores (ADR-0023)
                Permissions.Prefixes.Scope + Application.SecureGateScopes.Admin,
                Permissions.Prefixes.Scope + "logstream",
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Usuário operador de plataforma no tenant de plataforma, com o role platform-operator.</summary>
    private async Task SeedOperatorUserAsync(CancellationToken cancellationToken)
    {
        if (await userManager.FindByNameAsync(OperatorEmail).ConfigureAwait(false) is not null)
        {
            return;
        }

        var user = new Identity.User
        {
            Id = Guid.CreateVersion7(),
            TenantId = Application.SecureGatePlatform.TenantId,
            UserName = OperatorEmail,
            Email = OperatorEmail,
            EmailConfirmed = true,
        };

        if (!(await userManager.CreateAsync(user, OperatorPassword).ConfigureAwait(false)).Succeeded)
        {
            return;
        }

        var normalizedOperator = Application.SecureGatePlatform.OperatorRole.ToUpperInvariant();

        var role = await context.Roles
            .FirstOrDefaultAsync(
                r => r.TenantId == Application.SecureGatePlatform.TenantId && r.NormalizedName == normalizedOperator,
                cancellationToken)
            .ConfigureAwait(false);

        if (role is not null)
        {
            context.UserRoles.Add(new Identity.UserRole { UserId = user.Id, RoleId = role.Id });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Client web de DEV: authorization code + PKCE + refresh, público (sem secret), consent implícito.</summary>
    private async Task SeedWebClientAsync(CancellationToken cancellationToken)
    {
        if (await applicationManager.FindByClientIdAsync(DevWebClientId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = DevWebClientId,
            ClientType = ClientTypes.Public,
            // First-party confiável → sem tela de consent (Fase 6.5)
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Aplicação web de desenvolvimento Secco",
            RedirectUris = { new Uri("https://localhost/callback"), new Uri("http://localhost/callback") },
            PostLogoutRedirectUris = { new Uri("https://localhost/") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + "logstream",
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Usuário demo no tenant de desenvolvimento, com o role demo (para exercitar o login).</summary>
    private async Task SeedDevUserAsync(CancellationToken cancellationToken)
    {
        if (await userManager.FindByNameAsync(DevUserEmail).ConfigureAwait(false) is not null)
        {
            return;
        }

        var user = new Identity.User
        {
            Id = Guid.CreateVersion7(),
            TenantId = DemoTenantId,
            UserName = DevUserEmail,
            Email = DevUserEmail,
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, DevUserPassword).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return;
        }

        var normalizedRole = DevRoleName.ToUpperInvariant();

        var role = await context.Roles
            .FirstOrDefaultAsync(r => r.TenantId == DemoTenantId && r.NormalizedName == normalizedRole, cancellationToken)
            .ConfigureAwait(false);

        if (role is not null)
        {
            context.UserRoles.Add(new Identity.UserRole { UserId = user.Id, RoleId = role.Id });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Role demo do tenant de desenvolvimento com as permissões do LogStream.</summary>
    private async Task SeedDevRoleAsync(CancellationToken cancellationToken)
    {
        var normalized = DevRoleName.ToUpperInvariant();

        var role = await context.Roles
            .FirstOrDefaultAsync(r => r.TenantId == DemoTenantId && r.NormalizedName == normalized, cancellationToken)
            .ConfigureAwait(false);

        if (role is not null)
        {
            return;
        }

        role = new Identity.Role
        {
            Id = Guid.CreateVersion7(),
            TenantId = DemoTenantId,
            Name = DevRoleName,
            NormalizedName = normalized,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };

        context.Roles.Add(role);
        context.RoleClaims.AddRange(DevRolePermissions.Select(permission => new Identity.RoleClaim
        {
            RoleId = role.Id,
            ClaimType = Roles.RoleRepository.PermissionClaimType,
            ClaimValue = permission,
        }));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
