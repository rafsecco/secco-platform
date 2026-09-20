using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SendGrid;

namespace Secco.SDK.Email;

/// <summary>Composição de DI do envio de e-mail.</summary>
public static class SeccoEmailServiceCollectionExtensions
{
	/// <summary>Registra a porta de e-mail lendo a seção do produto (ADR-0033: o pacote entrega tipos, não a chave).</summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="sectionKey">Seção de configuração do produto (ex.: <c>SecureGate:Email</c>).</param>
	public static IServiceCollection AddSeccoEmail(this IServiceCollection services, string sectionKey)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

		services.AddOptions<SeccoEmailOptions>().BindConfiguration(sectionKey).ValidateOnStart();
		services.TryAddSingleton<IValidateOptions<SeccoEmailOptions>>(new SeccoEmailOptionsValidator(sectionKey));
		services.TryAddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<SeccoEmailOptions>>().Value);

		// Só é construído quando o provider selecionado é SendGrid — no SMTP não há chave de API.
		services.TryAddSingleton<ISendGridClient>(serviceProvider =>
			new SendGridClient(serviceProvider.GetRequiredService<SeccoEmailOptions>().ApiKey));

		services.TryAddScoped<ISeccoEmailSender>(serviceProvider =>
		{
			var options = serviceProvider.GetRequiredService<SeccoEmailOptions>();

			return options.Provider switch
			{
				SeccoEmailProvider.SendGrid => new SeccoSendGridEmailSender(serviceProvider.GetRequiredService<ISendGridClient>(), options),
				_ => new SeccoSmtpEmailSender(options),
			};
		});

		return services;
	}
}
