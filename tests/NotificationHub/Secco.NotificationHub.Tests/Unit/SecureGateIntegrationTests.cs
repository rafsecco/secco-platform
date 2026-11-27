using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.NotificationHub.Api.Extensions;
using Secco.SDK.AspNetCore.Authorization;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client.Authorization;
using Secco.SecureGate.Client.Catalog;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>
/// O NotificationHub lê tenants e permissões do SecureGate, como o LogStream (ADR-0005/0021): com a
/// seção <c>Secco:SecureGate</c> valem as implementações remotas; sem ela (DEV e testes), as de
/// configuração.
/// </summary>
public sealed class SecureGateIntegrationTests
{
	private static ServiceProvider Build(Dictionary<string, string?> settings)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddLogging();
		services.AddNotificationHubSecureGateIntegration();

		return services.BuildServiceProvider();
	}

	private static Dictionary<string, string?> SecureGateSection() => new()
	{
		["Secco:SecureGate:BaseUrl"] = "https://login.exemplo.com.br",
		["Secco:SecureGate:ClientId"] = "notificationhub-service",
		["Secco:SecureGate:ClientSecret"] = "segredo-de-teste-com-mais-de-32-caracteres",
		["Secco:SecureGate:Product"] = "notificationhub",
	};

	[Fact]
	public void TenantCatalog_ComSecaoDoSecureGate_UsaOCatalogoDoSecureGate()
	{
		using var provider = Build(SecureGateSection());

		provider.GetRequiredService<ITenantCatalog>().Should().BeOfType<SecureGateTenantCatalog>();
	}

	[Fact]
	public void PermissionResolver_ComSecaoDoSecureGate_UsaOResolverDoSecureGate()
	{
		using var provider = Build(SecureGateSection());

		provider.GetRequiredService<IPermissionResolver>().Should().BeOfType<SecureGatePermissionResolver>();
	}

	[Fact]
	public void TenantCatalog_SemSecaoDoSecureGate_UsaOCatalogoPorConfiguracao()
	{
		using var provider = Build([]);

		provider.GetRequiredService<ITenantCatalog>().Should().BeOfType<ConfigurationTenantCatalog>();
	}

	[Fact]
	public void PermissionResolver_SemSecaoDoSecureGate_UsaOResolverPorConfiguracao()
	{
		using var provider = Build([]);

		provider.GetRequiredService<IPermissionResolver>().Should().BeOfType<ConfigurationPermissionResolver>();
	}

	[Fact]
	public void TenantCatalog_SecaoSemProduct_RecusaSubir()
	{
		var settings = SecureGateSection();
		settings.Remove("Secco:SecureGate:Product");
		using var provider = Build(settings);

		var act = () => provider.GetRequiredService<ITenantCatalog>();

		act.Should().Throw<InvalidOperationException>().WithMessage("*Product*");
	}
}
