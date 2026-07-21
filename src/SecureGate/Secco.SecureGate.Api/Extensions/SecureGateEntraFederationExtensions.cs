using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Secco.SecureGate.Api.Identity;

namespace Secco.SecureGate.Api.Extensions;

/// <summary>
/// Composição do login federado com Entra ID (ADR-0026): um ÚNICO esquema OpenIdConnect
/// estático apontando para a app registration multi-tenant da plataforma. O Entra só prova
/// identidade — o esquema entrega o principal ao cookie EXTERNO do Identity e o
/// <see cref="EntraSignInProcessor"/> decide fail-closed; o SecureGate segue o único emissor
/// de tokens (ADR-0007/0022). Sem a seção <c>SecureGate:EntraId</c>, nada é registrado.
/// </summary>
public static class SecureGateEntraFederationExtensions
{
    /// <summary>Registra o esquema OIDC do Entra ID e o processador de login federado.</summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configuration">Configuração do host.</param>
    /// <param name="environment">Ambiente de hospedagem (define a política Secure dos cookies do handler).</param>
    public static IServiceCollection AddSecureGateEntraFederation(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.Configure<SecureGateEntraIdOptions>(configuration.GetSection(SecureGateEntraIdOptions.SectionName));

        // O processador é registrado sempre: a decisão fail-closed é testável sem o esquema
        services.AddScoped<EntraSignInProcessor>();

        var entra = configuration.GetSection(SecureGateEntraIdOptions.SectionName)
            .Get<SecureGateEntraIdOptions>() ?? new SecureGateEntraIdOptions();

        if (!entra.IsConfigured)
        {
            // Recurso desligado por ausência de configuração — mesmo padrão da seção
            // Secco:SecureGate nos produtos (ADR-0026): nada de esquema, botão não aparece
            return services;
        }

        var securePolicy = environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        services.AddAuthentication()
            .AddOpenIdConnect(SecureGateEntraIdOptions.AuthenticationScheme, options =>
            {
                options.Authority = entra.Authority;
                options.ClientId = entra.ClientId;
                options.ClientSecret = entra.ClientSecret;

                // Authorization code (com PKCE, default do handler para code)
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.CallbackPath = "/signin-entra";

                // O resultado vai ao cookie EXTERNO do Identity; a sessão real só nasce
                // depois da decisão fail-closed do EntraSignInProcessor (ADR-0026)
                options.SignInScheme = IdentityConstants.ExternalScheme;

                // Tokens do Entra não são custodiados: provada a identidade, são descartados
                options.SaveTokens = false;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                // Claims crus (tid/oid/email) — sem remapeamento legado (ADR-0007)
                options.MapInboundClaims = false;
                options.GetClaimsFromUserInfoEndpoint = false;

                // Multi-tenant (organizations): issuer varia por diretório — o validador
                // exige coerência issuer↔tid; o pin de diretório é do processor (ADR-0026)
                options.TokenValidationParameters.IssuerValidator = EntraIssuerValidator.Validate;
                options.TokenValidationParameters.ValidAudience = entra.ClientId;

                // Cookies transitórios do protocolo (correlation/nonce) seguem a política
                // do cookie de sessão (ADR-0020)
                options.CorrelationCookie.SecurePolicy = securePolicy;
                options.NonceCookie.SecurePolicy = securePolicy;

                options.Events = new OpenIdConnectEvents
                {
                    // Usuário cancelou no Microsoft, erro do diretório etc. — mensagem
                    // genérica na tela de login, nunca stack trace (ADR-0020)
                    OnRemoteFailure = context =>
                    {
                        context.Response.Redirect("/login?federatedError=1");
                        context.HandleResponse();
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }
}
