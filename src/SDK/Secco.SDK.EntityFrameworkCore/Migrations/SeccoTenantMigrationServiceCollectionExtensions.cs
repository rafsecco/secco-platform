using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>Registro da migração do tenant no primeiro uso (ADR-0038).</summary>
public static class SeccoTenantMigrationServiceCollectionExtensions
{
	/// <summary>
	/// Registra o gate (singleton, compartilhado) e o interceptor do contexto. O produto acrescenta
	/// o interceptor no seu <c>AddDbContext</c>. A fábrica cria um contexto SEM o interceptor.
	/// </summary>
	/// <typeparam name="TContext">Contexto de tenant do produto.</typeparam>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="createMigrationContext">Fábrica do contexto de migração (provider raiz, connection string).</param>
	public static IServiceCollection AddSeccoTenantMigrations<TContext>(
		this IServiceCollection services, Func<IServiceProvider, string, TContext> createMigrationContext)
		where TContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(createMigrationContext);

		services.TryAddSingleton(serviceProvider => new SeccoTenantMigrationGate(
			serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System));
		services.TryAddSingleton(serviceProvider => new SeccoTenantMigrationInterceptor<TContext>(
			serviceProvider.GetRequiredService<SeccoTenantMigrationGate>(), serviceProvider, createMigrationContext));

		return services;
	}
}
