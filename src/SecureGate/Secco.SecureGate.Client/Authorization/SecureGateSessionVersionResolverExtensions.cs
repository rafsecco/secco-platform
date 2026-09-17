using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.ClientCredentials;
using Secco.SecureGate.Client.Catalog;

namespace Secco.SecureGate.Client.Authorization;

/// <summary>Registro do resolvedor remoto de versão de sessão (ADR-0032).</summary>
public static class SecureGateSessionVersionResolverExtensions
{
	/// <summary>Usa a seção <c>Secco:SecureGate</c> — a mesma do catálogo e das permissões.</summary>
	/// <param name="services">Coleção de serviços.</param>
	public static IServiceCollection AddSecureGateSessionVersionResolver(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSecureGateClientCredentialsOptions();

		return services.AddSecureGateSessionVersionResolver(
			serviceProvider => serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>());
	}

	/// <summary>
	/// Usa credenciais próprias — para aplicação cujo client da seção <c>Secco:SecureGate</c> não deve ganhar
	/// client credentials (ex.: o AdminPortal, cujo client pode pedir <c>securegate:admin</c>).
	/// </summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="optionsFactory">Credenciais com acesso a <c>authorization:read</c>.</param>
	public static IServiceCollection AddSecureGateSessionVersionResolver(
		this IServiceCollection services,
		Func<IServiceProvider, SecureGateClientCredentialsOptions> optionsFactory)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(optionsFactory);

		// Store próprio: o token desta consulta não se mistura com o do catálogo nem com o de permissões
		var tokenStore = new SeccoAccessTokenStore();

		services.AddHttpClient(SecureGateSessionVersionResolver.HttpClientName)
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var options = optionsFactory(serviceProvider);

				if (options.IsConfigured)
				{
					client.BaseAddress = new Uri(options.BaseUrl!, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				var options = optionsFactory(serviceProvider);

				if (options.IsConfigured)
				{
					handlers.Add(new SeccoClientCredentialsHandler(
						options.BaseUrl!, options.ClientId!, options.ClientSecret!,
						SecureGateClientCredentialsOptions.AuthorizationScope, tokenStore));
				}
			});

		services.TryAddSingleton<ISessionVersionResolver>(serviceProvider =>
		{
			var options = optionsFactory(serviceProvider);

			if (options.IsConfigured)
			{
				options.Validate(requireProduct: false);
			}

			return new SecureGateSessionVersionResolver(serviceProvider.GetRequiredService<IHttpClientFactory>(), options);
		});

		return services;
	}
}
