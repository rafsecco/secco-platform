using Microsoft.AspNetCore.Identity;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Extensions;

/// <summary>
/// Registro do ASP.NET Identity para o login interativo (Fase 6.5, ADR-0022). Usa
/// <c>AddIdentityCore</c> (não <c>AddIdentity</c>): o esquema padrão da API continua sendo
/// o JwtBearer da ADR-0007 — o cookie do Identity é um esquema <b>não-default</b>, usado
/// apenas pelas telas de login e pelo endpoint de autorização. Sem isso, o cookie viraria
/// o esquema padrão e quebraria a validação Bearer dos endpoints de gestão.
/// </summary>
public static class SecureGateIdentityExtensions
{
    /// <summary>Registra UserManager/SignInManager e os cookies de login (esquema não-default).</summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSecureGateIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddIdentityCore<User>(options =>
            {
                // Política de senha (ADR-0020): mínimo forte, sem impor complexidade excessiva
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                // Bloqueio contra força bruta (ADR-0020): habilitado inclusive para novos usuários
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);

                // Login por e-mail: username = e-mail, único global (o registro carrega o tenant)
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<SecureGateDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Cookies do Identity como esquemas NÃO-DEFAULT (o default segue JwtBearer, ADR-0007).
        // O endpoint de autorização faz Challenge explícito no ApplicationScheme → tela de login.
        services.AddAuthentication()
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/connect/logout";
                options.AccessDeniedPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                options.SlidingExpiration = true;
                options.Cookie.Name = "secco.securegate.auth";
                // O fluxo de autorização é same-site (login e authorize no mesmo host)
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            })
            // Referenciado pelo SignInManager mesmo sem login externo configurado
            .AddCookie(IdentityConstants.ExternalScheme, options =>
                options.Cookie.Name = "secco.securegate.external");

        return services;
    }
}
