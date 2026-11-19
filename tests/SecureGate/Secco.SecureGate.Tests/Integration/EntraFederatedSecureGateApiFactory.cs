using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Secco.SecureGate.Api.Extensions;
using Secco.SecureGate.Infrastructure.Federation;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Variante da factory com o login federado (Entra ID) configurado, para o login federado direto: o esquema
/// <c>EntraId</c> só existe com <c>SecureGate:EntraId</c> preenchido. A configuração OIDC é fixa em memória —
/// nenhum teste fala com a Microsoft.
/// </summary>
public class EntraFederatedSecureGateApiFactory : SecureGateApiFactory
{
	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		base.ConfigureTestConfiguration(settings);

		ArgumentNullException.ThrowIfNull(settings);

		settings[$"{SecureGateEntraIdOptions.SectionName}:ClientId"] = "entra-client-de-teste";
		settings[$"{SecureGateEntraIdOptions.SectionName}:ClientSecret"] = "entra-secret-de-teste";
	}

	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		base.ConfigureTestServices(services);

		ArgumentNullException.ThrowIfNull(services);

		// A configuração em memória da factory chega DEPOIS de o Program.cs decidir se registra o esquema,
		// então ele não nasce sozinho: aplica-se a composição REAL do SecureGate com as mesmas chaves
		var entra = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[$"{SecureGateEntraIdOptions.SectionName}:ClientId"] = "entra-client-de-teste",
				[$"{SecureGateEntraIdOptions.SectionName}:ClientSecret"] = "entra-secret-de-teste",
			})
			.Build();
		services.AddSecureGateEntraFederation(entra, new TestEnvironment());

		// Configure (não PostConfigure): o post-configure do handler vê a Configuration e cria o
		// StaticConfigurationManager, então nada sai para a rede buscando o discovery.
		services.Configure<OpenIdConnectOptions>(
			SecureGateEntraIdOptions.AuthenticationScheme,
			options => options.Configuration = new OpenIdConnectConfiguration
			{
				AuthorizationEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize",
				TokenEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/token",
			});
	}
}

file sealed class TestEnvironment : IHostEnvironment
{
	public string EnvironmentName { get; set; } = Environments.Development;

	public string ApplicationName { get; set; } = "Secco.SecureGate.Api";

	public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

	public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
