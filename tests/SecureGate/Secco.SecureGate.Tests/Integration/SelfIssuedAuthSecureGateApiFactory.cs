using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Variante da factory em que o SecureGate valida OS PRÓPRIOS tokens: a Authority aponta
/// para o próprio servidor (discovery/JWKS pelo backchannel in-memory) — exatamente a
/// configuração de produção, onde os endpoints de catálogo/gestão exigem tokens emitidos
/// pelo <c>/connect/token</c>. Desliga a chave HS256 que a base monta.
/// </summary>
public class SelfIssuedAuthSecureGateApiFactory : SecureGateApiFactory
{
	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		base.ConfigureTestConfiguration(settings);

		ArgumentNullException.ThrowIfNull(settings);

		settings["Secco:Authentication:Authority"] = "http://localhost";
		settings["Secco:Authentication:RequireHttpsMetadata"] = "false";

		// Remover é a intenção explícita — antes isto dependia de "fontes posteriores vencem",
		// escrevendo "" por cima. Com o dicionário na mão, some de vez.
		settings.Remove("Secco:Authentication:DevelopmentSigningKey");
		settings.Remove("Secco:Authentication:Issuer");
	}

	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		base.ConfigureTestServices(services);

		ArgumentNullException.ThrowIfNull(services);

		// Configure (não PostConfigure) — mesma razão do teste cross-produto da 6.2.
		// O handler é lazy: o Server só pode ser tocado depois do host subir, e a
		// primeira validação de token acontece bem depois disso.
		services.Configure<JwtBearerOptions>(
			JwtBearerDefaults.AuthenticationScheme,
			options => options.BackchannelHttpHandler = new LazyBackchannelHandler(() => Server.CreateHandler()));
	}

	/// <summary>Cria o inner handler no primeiro uso — evita tocar <c>Server</c> durante a composição.</summary>
	private sealed class LazyBackchannelHandler(Func<HttpMessageHandler> handlerFactory) : DelegatingHandler
	{
		private readonly Lock _sync = new();

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			if (InnerHandler is null)
			{
				lock (_sync)
				{
					InnerHandler ??= handlerFactory();
				}
			}

			return base.SendAsync(request, cancellationToken);
		}
	}
}
