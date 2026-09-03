using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.LogStream.Application;
using Secco.LogStream.Application.ApiCalls;
using Secco.LogStream.Application.Audit;
using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Application.LogProcesses;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.LogStream.Infrastructure.Ingestion;
using Secco.LogStream.Infrastructure.Repositories;
using Secco.LogStream.Infrastructure.Retention;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.LogStream.Infrastructure;

/// <summary>Composição de DI da camada de infraestrutura do LogStream.</summary>
public static class LogStreamInfrastructureExtensions
{
	/// <summary>
	/// Registra o <see cref="LogStreamDbContext"/> apontando para o banco do tenant da
	/// requisição atual (ADR-0005): a connection string vem do <see cref="ITenantConnectionFactory"/>
	/// — jamais fixa. Requer <c>AddSeccoTenancy()</c> (via <c>AddSeccoPlatform()</c>).
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddLogStreamInfrastructure(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Bind LAZY nativo do framework: IOptions<T> só lê o IConfiguration do container
		// quando o valor é resolvido de fato — fontes adicionadas por testes/hosting tardio
		// são respeitadas, sem helper caseiro (ADR-0027).
		services.AddOptions<LogStreamDatabaseOptions>().BindConfiguration("LogStream:Database");
		services.AddOptions<LogStreamRetentionOptions>().BindConfiguration("LogStream:Retention");
		services.AddOptions<LogStreamIngestionOptions>().BindConfiguration("LogStream:Ingestion");

		// A camada Application recebe o POCO, nunca IOptions<T>: a csproj dela declara
		// "unica dependencia externa: abstracoes de DI" (ADR-0002/ADR-0003). O adaptador vive
		// aqui, na composicao, e preserva o bind lazy do framework.
		services.AddSingleton(serviceProvider =>
			serviceProvider.GetRequiredService<IOptions<LogStreamIngestionOptions>>().Value);

		services.AddHostedService<LogRetentionWorker>();

		services.AddDbContext<LogStreamDbContext>((serviceProvider, options) =>
		{
			var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
			var databaseOptions = serviceProvider.GetRequiredService<IOptions<LogStreamDatabaseOptions>>().Value;

			// O catálogo padrão resolve de forma síncrona (ValueTask já concluída);
			// catálogos remotos futuros devem manter cache para este caminho ser barato.
			var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

			LogStreamDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);
		});

		services.AddScoped<ILogEntryRepository, LogEntryRepository>();
		services.AddScoped<ILogProcessRepository, LogProcessRepository>();
		services.AddScoped<IApiCallLogRepository, ApiCallLogRepository>();
		services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();

		// Ingestão assíncrona: canal bounded compartilhado + adaptador por request + worker
		services.AddSingleton<LogEntryIngestionChannel>();
		services.AddScoped<ILogIngestionQueue, LogEntryIngestionQueue>();
		services.AddHostedService<LogEntryIngestionWorker>();

		return services;
	}

	/// <summary>
	/// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo.
	/// Uso: startup em Development e processos controlados de provisionamento (ADR-0005) —
	/// nunca no startup de produção.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task MigrateLogStreamTenantDatabasesAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
		var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<LogStreamDatabaseOptions>>().Value;

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = LogStreamDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new LogStreamDbContext(options);
			await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
