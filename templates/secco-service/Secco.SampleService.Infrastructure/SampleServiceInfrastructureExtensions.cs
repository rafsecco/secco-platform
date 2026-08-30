using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.SampleService.Application;
using Secco.SampleService.Application.Samples;
using Secco.SampleService.Infrastructure.Contexts;
using Secco.SampleService.Infrastructure.Repositories;
using Secco.SDK.AspNetCore.Tenancy;

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

		services.AddDbContext<SampleServiceDbContext>((serviceProvider, options) =>
		{
			var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
			var databaseOptions = serviceProvider.GetRequiredService<IOptions<SampleServiceDatabaseOptions>>().Value;

			// O catálogo padrão resolve de forma síncrona (ValueTask já concluída)
			var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

			SampleServiceDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);
		});

		services.AddScoped<ISampleRepository, SampleRepository>();

		return services;
	}

	/// <summary>
	/// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo.
	/// Uso: startup em Development e processos controlados de provisionamento (ADR-0005) —
	/// nunca no startup de produção.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task MigrateSampleServiceTenantDatabasesAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
		var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<SampleServiceDatabaseOptions>>().Value;

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = SampleServiceDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new SampleServiceDbContext(options);
			await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
