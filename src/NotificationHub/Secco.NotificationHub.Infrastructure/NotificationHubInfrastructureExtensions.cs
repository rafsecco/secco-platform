using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Infrastructure.Contexts;
using Secco.NotificationHub.Infrastructure.Email;
using Secco.NotificationHub.Infrastructure.Repositories;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using SendGrid;

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
		services.AddOptions<NotificationHubEmailOptions>()
			.BindConfiguration("NotificationHub:Email")
			.ValidateOnStart();
		services.TryAddSingleton<IValidateOptions<NotificationHubEmailOptions>, NotificationHubEmailOptionsValidator>();

		// A camada Application recebe o POCO, nunca IOptions<T>: a csproj dela declara
		// "única dependência externa: abstrações de DI" (ADR-0002/ADR-0003). O adaptador vive
		// aqui, na composição, e preserva o bind lazy do framework.
		services.AddSingleton(serviceProvider =>
			serviceProvider.GetRequiredService<IOptions<NotificationHubOptions>>().Value);
		services.AddSingleton(serviceProvider =>
			serviceProvider.GetRequiredService<IOptions<NotificationHubEmailOptions>>().Value);

		services.AddDbContext<NotificationHubDbContext>((serviceProvider, options) =>
		{
			var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
			var databaseOptions = serviceProvider.GetRequiredService<IOptions<NotificationHubDatabaseOptions>>().Value;

			// O catálogo padrão resolve de forma síncrona (ValueTask já concluída)
			var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

			NotificationHubDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);
		});

		services.AddScoped<INotificationRepository, NotificationRepository>();
		services.AddScoped<IInAppNotificationRepository, InAppNotificationRepository>();
		// Seleção do provider de e-mail (issue #14). O que troca é só a implementação da porta;
		// o job, o retry e o status seguem idênticos entre um provider e outro.
		services.AddScoped<IEmailSender>(serviceProvider =>
		{
			var emailOptions = serviceProvider.GetRequiredService<NotificationHubEmailOptions>();

			return emailOptions.Provider switch
			{
				NotificationHubEmailProvider.SendGrid => new SendGridEmailSender(
					serviceProvider.GetRequiredService<ISendGridClient>(), emailOptions),
				_ => new MailKitEmailSender(emailOptions),
			};
		});

		// O client do SendGrid é singleton por envolver HttpClient; só é construído se o
		// provider selecionado for esse — a chave de API não precisa existir no caso SMTP.
		services.AddSingleton<ISendGridClient>(serviceProvider =>
			new SendGridClient(serviceProvider.GetRequiredService<NotificationHubEmailOptions>().ApiKey));
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
	/// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo.
	/// Uso: startup em Development e processos controlados de provisionamento (ADR-0005) —
	/// nunca no startup de produção.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task MigrateNotificationHubTenantDatabasesAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
		var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<NotificationHubDatabaseOptions>>().Value;

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = NotificationHubDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new NotificationHubDbContext(options);
			await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
