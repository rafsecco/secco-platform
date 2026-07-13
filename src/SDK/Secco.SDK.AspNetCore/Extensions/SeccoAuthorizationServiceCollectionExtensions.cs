using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SDK.AspNetCore.Authorization;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI da autorização granular Role + Permission (ADR-0021).</summary>
public static class SeccoAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registra a autorização granular da plataforma (ADR-0021): policies dinâmicas por
    /// permissão (<c>RequireAuthorization("recurso:acao")</c> com as constantes do produto),
    /// resolução <c>(tenant, role) → permissões</c> com cache obrigatório de TTL curto
    /// (<c>Secco:Authorization:CacheTtlSeconds</c>, padrão 60s) e postura <b>fail-closed</b>.
    /// Resolver padrão lê de <c>IConfiguration</c> (DEV/testes); em produção, registrar a
    /// resolução remota do SecureGate (<c>AddSecureGatePermissionResolver()</c> do pacote
    /// <c>Secco.SecureGate.Client</c>). Incluído no <c>AddSeccoPlatform()</c>.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">Ajuste fino opcional, aplicado após o bind da configuração.</param>
    public static IServiceCollection AddSeccoAuthorization(
        this IServiceCollection services,
        Action<SeccoAuthorizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<SeccoAuthorizationOptions>()
            .BindConfiguration(SeccoAuthorizationOptions.SectionKey)
            .Validate(options => options.CacheTtlSeconds > 0,
                $"'{SeccoAuthorizationOptions.SectionKey}:CacheTtlSeconds' deve ser maior que zero.")
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // Replace, não TryAdd: o AddAuthorization() do framework (via AddSeccoAuthentication)
        // já registrou o provider padrão — o nosso o envolve e delega o que não é permissão
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, SeccoPermissionPolicyProvider>());
        services.TryAddSingleton<IPermissionResolver, ConfigurationPermissionResolver>();
        services.TryAddSingleton<CachedPermissionResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, PermissionAuthorizationHandler>());

        return services;
    }
}
