using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.SampleService.Application;
using Secco.SampleService.Application.Samples;
using Secco.SampleService.Infrastructure.Contexts;
using Secco.SampleService.Infrastructure.Repositories;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Migrations;

namespace Secco.SampleService.Infrastructure;

/// <summary>Composição de DI da camada de infraestrutura.</summary>
public static class SampleServiceInfrastructureExtensions
{
	/// <summary>
	/// Registra o <see cref="SampleServiceDbContext"/> apontando para o banco do tenant da
	/// requisição atual (ADR-0005): a connection string vem do <see cref="ITenantConnectionFactory"/>
	/// — jamais fixa. Requer <c>AddSeccoTenancy()</c> (via <c>AddSeccoPlatform()</c>).
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddSampleServiceInfrastructure(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Bind LAZY (do IConfiguration do DI): fontes adicionadas por testes/hosting tardio são respeitadas
		services.AddOptions<SampleServiceDatabaseOptions>().BindConfiguration("SampleService:Database");
		services.AddOptions<SampleServiceOptions>().BindConfiguration("SampleService:Limits");

		// A camada Application recebe o POCO, nunca IOptions<T>: a csproj dela declara
		// "única dependência externa: abstrações de DI" (ADR-0002/ADR-0003). O adaptador vive
		// aqui, na composição, e preserva o bind lazy do framework.
		services.AddSingleton(serviceProvider =>
			serviceProvider.GetRequiredService<IOptions<SampleServiceOptions>>().Value);

		// Processo controlado (ADR-0038) e migração do tenant novo no primeiro uso
		services.AddScoped<ISeccoDatabaseMigrator, SampleServiceTenantMigrator>();
		services.AddSeccoTenantMigrations((serviceProvider, connectionString) =>
			// Sem interceptor: este contexto só serve para migrar
			new SampleServiceDbContext(SampleServiceDatabaseProviderConfigurator.CreateOptions(
				serviceProvider.GetRequiredService<IOptions<SampleServiceDatabaseOptions>>().Value.Provider,
				connectionString)));

		services.AddDbContext<SampleServiceDbContext>((serviceProvider, options) =>
		{
			var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
			var databaseOptions = serviceProvider.GetRequiredService<IOptions<SampleServiceDatabaseOptions>>().Value;

			// O catálogo padrão resolve de forma síncrona (ValueTask já concluída)
			var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

			SampleServiceDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);

			options.AddInterceptors(serviceProvider.GetRequiredService<SeccoTenantMigrationInterceptor<SampleServiceDbContext>>());
		});

		services.AddScoped<ISampleRepository, SampleRepository>();

		return services;
	}

	/// <summary>
	/// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo, via o migrator
	/// do produto (ADR-0038); lança se algum tenant falhar. Uso: fábricas de teste e provisionamento.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task MigrateSampleServiceTenantDatabasesAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var migrator = ActivatorUtilities.CreateInstance<SampleServiceTenantMigrator>(scope.ServiceProvider);
		var failures = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

		if (failures.Count > 0)
		{
			throw new InvalidOperationException(
				$"Migrations do SampleService falharam em {failures.Count} tenant(s): {string.Join(", ", failures)}.");
		}
	}
}
