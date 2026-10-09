using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.NotificationHub.Infrastructure;
using Secco.SDK.Email;
using Secco.SDK.Testing;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV) sobre a
/// base da plataforma (ADR-0027): bancos de tenant (ADR-0005) e o banco de plataforma do
/// Hangfire (ADR-0015), todos na mesma instância. O envio de e-mail é substituído por
/// <see cref="FakeEmailSender"/> — não há SMTP real em teste.
/// </summary>
public sealed class NotificationHubApiFactory : SeccoApiFactory<Program>
{
	private const string PlatformDatabaseName = "secco_notificationhub_platform";

	/// <inheritdoc />
	protected override string Audience => "secco-notificationhub";

	/// <summary>Primeiro tenant do catálogo de testes.</summary>
	public Guid TenantAlfa { get; } = Guid.NewGuid();

	/// <summary>Segundo tenant — existe para provar que nenhuma query cruza bancos.</summary>
	public Guid TenantBeta { get; } = Guid.NewGuid();

	/// <summary>
	/// Tenant provisionado DEPOIS do deploy (ADR-0028/ADR-0038): o banco existe e está vazio, e
	/// <see cref="MigrateAsync"/> NÃO o migra — o primeiro uso é que cria o schema. Vive na factory
	/// compartilhada porque uma segunda factory no processo quebra o bridge estático de log do
	/// Hangfire (ver <see cref="NotificationHubApiCollectionDefinition"/>).
	/// </summary>
	public Guid TenantNovo { get; } = Guid.NewGuid();

	/// <summary>Nome-base do banco do <see cref="TenantNovo"/>.</summary>
	public const string NewTenantDatabaseName = "secco_notificationhub_novo";

	/// <inheritdoc />
	protected override async Task MigrateAsync(IServiceProvider services)
	{
		// Alfa e Beta migrados na hora; o TenantNovo fica de fora de propósito
		foreach (var connectionString in new[] { GetConnectionStringFor("secco_notificationhub_alfa"), GetConnectionStringFor("secco_notificationhub_beta") })
		{
			await using var context = new Secco.NotificationHub.Infrastructure.Contexts.NotificationHubDbContext(
				NotificationHubDatabaseProviderConfigurator.CreateOptions(NotificationHubDatabaseProvider.SqlServer, connectionString));
			await context.Database.MigrateAsync();
		}
	}

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		AddTenant(settings, TenantAlfa, GetConnectionStringFor("secco_notificationhub_alfa"));
		AddTenant(settings, TenantBeta, GetConnectionStringFor("secco_notificationhub_beta"));
		AddTenant(settings, TenantNovo, GetConnectionStringFor(NewTenantDatabaseName));

		AddRolePermissions(
			settings,
			DefaultTestRole,
			"notifications:read",
			"notifications:write",
			"in-app-notifications:read",
			"in-app-notifications:write");

		// A configuração de e-mail é validada no startup desde a issue #14 (fail-fast, ADR-0020).
		// O envio em si nunca acontece — o ISeccoEmailSender é substituído por um fake logo abaixo —,
		// mas o host precisa subir, e um deployment real sempre configura isto.
		settings["NotificationHub:Email:FromAddress"] = "no-reply@secco.test";
		settings["NotificationHub:Email:Host"] = "localhost";

		// Banco de PLATAFORMA do Hangfire (ADR-0015) — nunca por tenant
		settings["NotificationHub:BackgroundJobs:ConnectionString"] =
			GetConnectionStringFor(PlatformDatabaseName);
	}

	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		// Sem SMTP real em teste: substitui o sender real pelo fake (ADR-0012)
		services.AddScoped<ISeccoEmailSender, FakeEmailSender>();
	}

	/// <summary>
	/// Diferente das migrations do EF Core (que criam o banco de tenant sozinhas), o Hangfire
	/// só cria o SCHEMA dentro de um banco já existente — o banco de plataforma precisa
	/// existir antes do primeiro <c>Enqueue</c>.
	/// </summary>
	protected override async Task OnInitializedAsync()
	{
		await CreateDatabaseAsync(PlatformDatabaseName);
		await CreateDatabaseAsync(NewTenantDatabaseName);
	}
}
