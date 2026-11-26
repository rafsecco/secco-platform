using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Secco.SDK.AspNetCore.ReverseProxy;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Suporte opt-in a proxy reverso que termina TLS (ADR-0062).</summary>
public static class SeccoReverseProxyExtensions
{
	/// <summary>
	/// Liga <c>Secco:ReverseProxy</c> (validada na subida) e configura os cabeçalhos encaminhados:
	/// só <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c>, um salto, apenas das redes configuradas.
	/// Chamadas repetidas são no-op.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddSeccoForwardedHeaders(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddOptions<SeccoReverseProxyOptions>()
			.BindConfiguration(SeccoReverseProxyOptions.SectionKey)
			.ValidateOnStart();

		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IValidateOptions<SeccoReverseProxyOptions>, SeccoReverseProxyOptionsValidator>());
		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IConfigureOptions<ForwardedHeadersOptions>, ConfigureForwardedHeaders>());

		return services;
	}

	/// <summary>
	/// Aplica <c>UseForwardedHeaders()</c> só quando <c>Secco:ReverseProxy:Enabled</c> é verdadeiro;
	/// caso contrário é no-op. Deve ser o primeiro middleware do pipeline.
	/// </summary>
	/// <param name="app">Pipeline da aplicação.</param>
	public static IApplicationBuilder UseSeccoForwardedHeaders(this IApplicationBuilder app)
	{
		ArgumentNullException.ThrowIfNull(app);

		var options = app.ApplicationServices.GetRequiredService<IOptions<SeccoReverseProxyOptions>>().Value;

		return options.Enabled ? app.UseForwardedHeaders() : app;
	}

	private sealed class ConfigureForwardedHeaders(IOptions<SeccoReverseProxyOptions> proxy)
		: IConfigureOptions<ForwardedHeadersOptions>
	{
		public void Configure(ForwardedHeadersOptions options)
		{
			var value = proxy.Value;

			if (!value.Enabled)
			{
				return;
			}

			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			options.ForwardLimit = 1;
			options.KnownIPNetworks.Clear();
			options.KnownProxies.Clear();

			foreach (var cidr in value.KnownNetworks)
			{
				options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
			}
		}
	}
}
