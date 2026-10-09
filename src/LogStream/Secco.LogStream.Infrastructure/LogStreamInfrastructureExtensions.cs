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
using Secco.SDK.EntityFrameworkCore.Migrations;

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

		// Processo controlado (ADR-0038) e migração do tenant novo no primeiro uso
		services.AddScoped<ISeccoDatabaseMigrator, LogStreamTenantMigrator>();
		services.AddSeccoTenantMigrations((serviceProvider, connectionString) =>
			// Sem interceptor: este contexto só serve para migrar
			new LogStreamDbContext(LogStreamDatabaseProviderConfigurator.CreateOptions(
				serviceProvider.GetRequiredService<IOptions<LogStreamDatabaseOptions>>().Value.Provider,
				connectionString)));

		services.AddDbContext<LogStreamDbContext>((serviceProvider, options) =>
		{
			var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
			var databaseOptions = serviceProvider.GetRequiredService<IOptions<LogStreamDatabaseOptions>>().Value;

			// O catálogo padrão resolve de forma síncrona (ValueTask já concluída);
			// catálogos remotos futuros devem manter cache para este caminho ser barato.
			var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

			LogStreamDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);

			options.AddInterceptors(serviceProvider.GetRequiredService<SeccoTenantMigrationInterceptor<LogStreamDbContext>>());
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
	/// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo, via o migrator
	/// do produto (ADR-0038); lança se algum tenant falhar. Uso: fábricas de teste e provisionamento.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task MigrateLogStreamTenantDatabasesAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var migrator = ActivatorUtilities.CreateInstance<LogStreamTenantMigrator>(scope.ServiceProvider);
		var failures = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

		if (failures.Count > 0)
		{
			throw new InvalidOperationException(
				$"Migrations do LogStream falharam em {failures.Count} tenant(s): {string.Join(", ", failures)}.");
		}
	}
}
