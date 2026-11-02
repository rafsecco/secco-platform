using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Secco.SDK.Documents;

/// <summary>Composição de DI da exportação de listagens (ADR-0058).</summary>
public static class SeccoDocumentsServiceCollectionExtensions
{
	/// <summary>Registra o exportador lendo a seção do produto (o pacote entrega tipos, não a chave).</summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="sectionKey">Seção de configuração do produto (ex.: <c>LogStream:Documents</c>).</param>
	public static IServiceCollection AddSeccoDocuments(this IServiceCollection services, string sectionKey)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

		services.AddOptions<SeccoDocumentsOptions>().BindConfiguration(sectionKey).ValidateOnStart();
		services.TryAddSingleton<IValidateOptions<SeccoDocumentsOptions>>(new SeccoDocumentsOptionsValidator(sectionKey));
		services.TryAddSingleton(TimeProvider.System);

		// Singleton: a vaga de concorrência é do processo. Fábrica explícita: o construtor é internal.
		services.TryAddSingleton<ISeccoDocumentExporter>(serviceProvider => new SeccoDocumentExporter(
			serviceProvider.GetRequiredService<IOptions<SeccoDocumentsOptions>>(),
			serviceProvider.GetRequiredService<TimeProvider>()));

		return services;
	}
}
