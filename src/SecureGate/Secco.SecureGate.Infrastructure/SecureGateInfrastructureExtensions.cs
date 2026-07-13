using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Seeding;

namespace Secco.SecureGate.Infrastructure;

/// <summary>Composição de DI da camada de infraestrutura do SecureGate.</summary>
public static class SecureGateInfrastructureExtensions
{
    /// <summary>
    /// Registra o <see cref="SecureGateDbContext"/> sobre o banco de plataforma
    /// (<c>SecureGate:Database:ConnectionString</c> — bind lazy do <c>IConfiguration</c>).
    /// Identidade não é dado de tenant (ADR-0022): sem catálogo, connection string única.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSecureGateInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var options = new SecureGateDatabaseOptions();
            sp.GetRequiredService<IConfiguration>().GetSection("SecureGate:Database").Bind(options);

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    "'SecureGate:Database:ConnectionString' é obrigatória — o banco de plataforma do SecureGate não é resolvido por catálogo (ADR-0022).");
            }

            return options;
        });

        services.AddDbContext<SecureGateDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<SecureGateDatabaseOptions>();

            SecureGateDatabaseProviderConfigurator.Configure(
                options, databaseOptions.Provider, databaseOptions.ConnectionString!);
        });

        // Catálogo de tenants (Fase 6.3)
        services.AddScoped<Application.Tenants.ITenantRepository, Tenants.TenantRepository>();

        // Roles + permissões (Fase 6.4, ADR-0021)
        services.AddScoped<Application.Roles.IRoleRepository, Roles.RoleRepository>();

        // Seeding (ADR-0019): scopes de produto (referência) + tenant/client demo (DEV)
        services.AddScoped<IReferenceDataSeeder, SecureGateReferenceDataSeeder>();
        services.AddScoped<IDevelopmentDataSeeder, SecureGateDevelopmentDataSeeder>();

        return services;
    }

    /// <summary>
    /// Aplica as migrations pendentes no banco de plataforma. Uso: startup em Development
    /// e processo controlado de deploy — nunca no startup de produção (ADR-0005).
    /// </summary>
    /// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task MigrateSecureGateDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var databaseOptions = scope.ServiceProvider.GetRequiredService<SecureGateDatabaseOptions>();

        var options = SecureGateDatabaseProviderConfigurator.CreateOptions(
            databaseOptions.Provider, databaseOptions.ConnectionString!);

        await using var context = new SecureGateDbContext(options);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
