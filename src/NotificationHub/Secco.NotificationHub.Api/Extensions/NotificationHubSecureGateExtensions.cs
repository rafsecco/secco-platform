using Secco.SecureGate.Client.Authorization;
using Secco.SecureGate.Client.Catalog;

namespace Secco.NotificationHub.Api.Extensions;

/// <summary>
/// Liga o NotificationHub ao SecureGate como o LogStream: catálogo de tenants central (ADR-0005) e
/// permissões resolvidas por perfil no SecureGate (ADR-0021), com a versão de sessão conferida
/// (ADR-0032). A decisão é lazy: com a seção <c>Secco:SecureGate</c> configurada valem as
/// implementações remotas; sem ela (DEV e testes), o catálogo e os perfis por configuração.
/// </summary>
public static class NotificationHubSecureGateExtensions
{
	/// <summary>Registra o catálogo de tenants e o resolvedor de permissões do SecureGate.</summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddNotificationHubSecureGateIntegration(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSecureGateTenantCatalog();
		services.AddSecureGatePermissionResolver();

		return services;
	}
}
