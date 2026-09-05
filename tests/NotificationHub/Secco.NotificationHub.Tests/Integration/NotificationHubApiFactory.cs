using Microsoft.Extensions.DependencyInjection;
using Secco.NotificationHub.Infrastructure;
using Secco.NotificationHub.Infrastructure.Email;
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

	/// <inheritdoc />
	protected override Task MigrateAsync(IServiceProvider services) =>
		services.MigrateNotificationHubTenantDatabasesAsync();

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		AddTenant(settings, TenantAlfa, GetConnectionStringFor("secco_notificationhub_alfa"));
		AddTenant(settings, TenantBeta, GetConnectionStringFor("secco_notificationhub_beta"));

		AddRolePermissions(
			settings,
			DefaultTestRole,
			"notifications:read",
			"notifications:write",
			"in-app-notifications:read",
			"in-app-notifications:write");

		// A configuração de e-mail é validada no startup desde a issue #14 (fail-fast, ADR-0020).
		// O envio em si nunca acontece — o IEmailSender é substituído por um fake logo abaixo —,
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
		services.AddScoped<IEmailSender, FakeEmailSender>();
	}

	/// <summary>
	/// Diferente das migrations do EF Core (que criam o banco de tenant sozinhas), o Hangfire
	/// só cria o SCHEMA dentro de um banco já existente — o banco de plataforma precisa
	/// existir antes do primeiro <c>Enqueue</c>.
	/// </summary>
	protected override Task OnInitializedAsync() => CreateDatabaseAsync(PlatformDatabaseName);
}
