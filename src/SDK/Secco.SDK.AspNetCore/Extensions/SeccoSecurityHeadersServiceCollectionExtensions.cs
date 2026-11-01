using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Secco.SDK.AspNetCore.SecurityHeaders;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI para os cabeçalhos de segurança de resposta (ADR-0057).</summary>
public static class SeccoSecurityHeadersServiceCollectionExtensions
{
	/// <summary>
	/// Registra a política de cabeçalhos de segurança, validada na subida (fail-fast), e o
	/// <see cref="ISeccoCspNonce"/>. Requer <c>UseSeccoSecurityHeaders()</c> no pipeline.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configure">Ajustes do produto sobre os padrões seguros.</param>
	public static IServiceCollection AddSeccoSecurityHeaders(
		this IServiceCollection services,
		Action<SeccoSecurityHeadersOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		var options = services.AddOptions<SeccoSecurityHeadersOptions>();

		if (configure is not null)
		{
			options.Configure(configure);
		}

		options.ValidateOnStart();

		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IValidateOptions<SeccoSecurityHeadersOptions>, SeccoSecurityHeadersOptionsValidator>());
		services.TryAddSingleton(sp => SeccoSecurityHeadersPolicy.Create(
			sp.GetRequiredService<IOptions<SeccoSecurityHeadersOptions>>().Value));
		services.AddHttpContextAccessor();
		services.TryAddScoped<ISeccoCspNonce, SeccoCspNonce>();

		return services;
	}
}
