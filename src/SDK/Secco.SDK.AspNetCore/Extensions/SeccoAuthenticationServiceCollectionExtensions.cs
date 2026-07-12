using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Secco.SDK.AspNetCore.Authentication;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI para autenticação JWT/OIDC da plataforma (ADR-0007).</summary>
public static class SeccoAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Registra a autenticação JWT Bearer conforme a ADR-0007 (claims curtas, sem remapeamento;
    /// Authority/JWKS em produção — SecureGate na Fase 6 — ou chave HS256 fora de Production) e a
    /// postura fail-closed da ADR-0020: <c>FallbackPolicy</c> exige usuário autenticado em todo
    /// endpoint sem metadata explícita (health checks são <c>AllowAnonymous</c> pelo SDK).
    /// Configuração pela seção <c>Secco:Authentication</c>, validada no startup (fail-fast).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">Ajuste fino opcional, aplicado após o bind da configuração.</param>
    public static IServiceCollection AddSeccoAuthentication(
        this IServiceCollection services,
        Action<SeccoAuthenticationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<SeccoAuthenticationOptions>()
            .BindConfiguration(SeccoAuthenticationOptions.SectionKey)
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<IValidateOptions<SeccoAuthenticationOptions>, SeccoAuthenticationOptionsValidator>();
        services.TryAddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureSeccoJwtBearerOptions>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
