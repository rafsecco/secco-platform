using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SecureGate.Client.Catalog;

namespace Secco.SecureGate.Client.Permissions;

/// <summary>Composição de DI do publicador do catálogo de permissões (ADR-0039).</summary>
public static class SecureGatePermissionCatalogPublisherExtensions
{
	/// <summary>
	/// Registra o <see cref="ISecureGatePermissionCatalogPublisher"/>. Mesma decisão lazy dos demais
	/// recursos do client: sem a seção <c>Secco:SecureGate</c> a publicação é pulada (compose, DEV
	/// isolado); com a seção, o client da plataforma precisa do escopo <c>permissions:&lt;produto&gt;</c>.
	/// Chamar após <c>AddSeccoPlatform()</c>.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddSecureGatePermissionCatalogPublisher(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSecureGateClientCredentialsOptions();
		services.AddHttpClient(SecureGatePermissionCatalogPublisher.HttpClientName);
		services.TryAddScoped<ISecureGatePermissionCatalogPublisher, SecureGatePermissionCatalogPublisher>();

		return services;
	}
}
