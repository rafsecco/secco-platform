using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SDK.Imaging.Internal;

namespace Secco.SDK.Imaging;

/// <summary>Composição de DI do processamento de imagens.</summary>
public static class SeccoImagingServiceCollectionExtensions
{
	/// <summary>Registra o processador de imagens lendo a seção do produto (ADR-0033: o pacote entrega tipos, não a chave).</summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="sectionKey">Seção de configuração do produto (ex.: <c>Intranet:Imaging</c>).</param>
	public static IServiceCollection AddSeccoImaging(this IServiceCollection services, string sectionKey)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

		services.AddOptions<SeccoImagingOptions>().BindConfiguration(sectionKey).ValidateOnStart();
		services.TryAddSingleton<IValidateOptions<SeccoImagingOptions>>(new SeccoImagingOptionsValidator(sectionKey));

		// Singleton: a vaga de concorrência (semáforo) é compartilhada por todas as chamadas do processo.
		services.TryAddSingleton<IImageCodec, SkiaImageCodec>();
		// Fábrica explícita: o construtor é internal e o contêiner só enxerga construtores públicos.
		services.TryAddSingleton<ISeccoImageProcessor>(serviceProvider => new SeccoImageProcessor(
			serviceProvider.GetRequiredService<IImageCodec>(),
			serviceProvider.GetRequiredService<IOptions<SeccoImagingOptions>>(),
			serviceProvider.GetRequiredService<ILogger<SeccoImageProcessor>>()));

		return services;
	}
}
