using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Secco.SharedKernel.Constants;

namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Composição da autenticação do AdminPortal como relying party OIDC (ADR-0023): cookie de
/// sessão + authorization code/PKCE contra o SecureGate. NÃO usa <c>AddSeccoAuthentication()</c>
/// (validação JWT de resource server) — o AdminPortal é um CLIENTE, não um resource server.
/// </summary>
public static class AdminPortalAuthenticationExtensions
{
    /// <summary>Registra cookie + OpenIdConnect e a policy de operador.</summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configuration">Configuração do host (seção <c>Secco:SecureGate</c>).</param>
    /// <param name="environment">Ambiente de hospedagem.</param>
    public static IServiceCollection AddAdminPortalAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "secco.adminportal.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = configuration["Secco:SecureGate:Authority"];
                options.ClientId = configuration["Secco:SecureGate:ClientId"];
                options.ClientSecret = configuration["Secco:SecureGate:ClientSecret"];

                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;

                // O access token é custodiado como claim no cookie (ver OnTokenValidated) —
                // acessível no circuito do Blazor Server via AuthenticationStateProvider
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false; // as claims já vêm no id_token

                // ADR-0007: claims curtas sem remapeamento; name = sub/username, role = 'role'
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = SeccoClaims.Role;

                // Fora de Production o discovery pode ser HTTP (dev local)
                options.RequireHttpsMetadata = environment.IsProduction();

                options.Scope.Clear();
                foreach (var scope in AdminPortalDefaults.Scopes)
                {
                    options.Scope.Add(scope);
                }

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = context =>
                    {
                        // Custódia server-side do access token do operador (ADR-0020/0023)
                        if (context.TokenEndpointResponse?.AccessToken is { Length: > 0 } accessToken
                            && context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            identity.AddClaim(new Claim(AdminPortalDefaults.AccessTokenClaim, accessToken));
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(options =>
            options.AddPolicy(AdminPortalDefaults.OperatorPolicy, policy =>
                policy.RequireRole(AdminPortalDefaults.OperatorRole)));

        services.AddScoped<IOperatorTokenProvider, OperatorTokenProvider>();

        return services;
    }
}
