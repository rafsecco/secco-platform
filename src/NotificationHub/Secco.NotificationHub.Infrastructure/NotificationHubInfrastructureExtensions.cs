using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Infrastructure.Contexts;
using Secco.NotificationHub.Infrastructure.Email;
using Secco.NotificationHub.Infrastructure.Repositories;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Secco.SDK.Email;

namespace Secco.NotificationHub.Infrastructure;

/// <summary>Composição de DI da camada de infraestrutura.</summary>
public static class NotificationHubInfrastructureExtensions
{
	/// <summary>
	/// Registra o <see cref="NotificationHubDbContext"/> apontando para o banco do tenant da
	/// requisição atual (ADR-0005): a connection string vem do <see cref="ITenantConnectionFactory"/>
	/// — jamais fixa. Requer <c>AddSeccoTenancy()</c> (via <c>AddSeccoPlatform()</c>).
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddNotificationHubInfrastructure(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Bind LAZY nativo do framework: IOptions<T> só lê o IConfiguration do container quando o
		// valor é resolvido — fontes adicionadas por testes/hosting tardio são respeitadas, sem
		// helper caseiro (ADR-0027).
		services.AddOptions<NotificationHubDatabaseOptions>().BindConfiguration("NotificationHub:Database");
		services.AddOptions<NotificationHubOptions>().BindConfiguration("NotificationHub:Limits");

		// A camada Application recebe o POCO, nunca IOptions<T>: a csproj dela declara
		// "única dependência externa: abstrações de DI" (ADR-0002/ADR-0003). O adaptador vive
		// aqui, na composição, e preserva o bind lazy do framework.
		services.AddSingleton(serviceProvider =>
			serviceProvider.GetRequiredService<IOptions<NotificationHubOptions>>().Value);

		// Envio de e-mail (ADR-0033): porta e adaptadores promovidos ao Secco.SDK.Email — a
		// seção de configuração continua sendo NotificationHub:Email, sem quebra.
		services.AddSeccoEmail("NotificationHub:Email");

		// Processo controlado (ADR-0038) e migração do tenant novo no primeiro uso
		services.AddScoped<ISeccoDatabaseMigrator, NotificationHubTenantMigrator>();
		services.AddSeccoTenantMigrations((serviceProvider, connectionString) =>
			// Sem interceptor: este contexto só serve para migrar
			new NotificationHubDbContext(NotificationHubDatabaseProviderConfigurator.CreateOptions(
				serviceProvider.GetRequiredService<IOptions<NotificationHubDatabaseOptions>>().Value.Provider,
				connectionString)));

		services.AddDbContext<NotificationHubDbContext>((serviceProvider, options) =>
		{
			var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
			var databaseOptions = serviceProvider.GetRequiredService<IOptions<NotificationHubDatabaseOptions>>().Value;

			// O catálogo padrão resolve de forma síncrona (ValueTask já concluída)
			var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

			NotificationHubDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);

			options.AddInterceptors(serviceProvider.GetRequiredService<SeccoTenantMigrationInterceptor<NotificationHubDbContext>>());
		});

		services.AddScoped<INotificationRepository, NotificationRepository>();
		services.AddScoped<IInAppNotificationRepository, InAppNotificationRepository>();

		// Canais externos (ADR-0029). Um provider por ferramenta, cada um com HttpClient nomeado
		// que herda a resiliência do SDK. Não há provider genérico por baixo, de propósito.
		services.AddHttpClient(Channels.TeamsNotificationProvider.HttpClientName);
		services.AddHttpClient(Channels.SlackNotificationProvider.HttpClientName);
		services.AddScoped<Channels.IExternalChannelProvider, Channels.TeamsNotificationProvider>();
		services.AddScoped<Channels.IExternalChannelProvider, Channels.SlackNotificationProvider>();
		services.AddScoped<Application.Channels.IExternalChannelDispatchQueue, Channels.ExternalChannelDispatchScheduler>();
		services.AddScoped<Application.Channels.IChannelConfigurationRepository, Repositories.ChannelConfigurationRepository>();
		services.AddScoped<Channels.SendExternalChannelJob>();
		services.AddScoped<IEmailDispatchQueue, EmailDispatchScheduler>();
		services.AddScoped<SendEmailJob>();

		return services;
	}

	/// <summary>
	/// Registra o Hangfire (ADR-0015 Camada 2) com storage no banco de PLATAFORMA — nunca
	/// por tenant. Chamada separada de <see cref="AddNotificationHubInfrastructure"/> só por
	/// clareza de composição (o wiring é conceitualmente distinto: fila, não dados de tenant).
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddNotificationHubBackgroundJobs(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddOptions<NotificationHubBackgroundJobOptions>()
			.BindConfiguration("NotificationHub:BackgroundJobs");

		services.AddSeccoBackgroundJobs(serviceProvider =>
			serviceProvider.GetRequiredService<IOptions<NotificationHubBackgroundJobOptions>>()
				.Value.ConnectionString);

		return services;
	}

	/// <summary>
	/// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo, via o migrator
	/// do produto (ADR-0038); lança se algum tenant falhar. Uso: fábricas de teste e provisionamento.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task MigrateNotificationHubTenantDatabasesAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var migrator = ActivatorUtilities.CreateInstance<NotificationHubTenantMigrator>(scope.ServiceProvider);
		var failures = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

		if (failures.Count > 0)
		{
			throw new InvalidOperationException(
				$"Migrations do NotificationHub falharam em {failures.Count} tenant(s): {string.Join(", ", failures)}.");
		}
	}
}
