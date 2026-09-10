using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Secco.SDK.ClientCredentials.Tests;

/// <summary>
/// Composição de DI (<see cref="SeccoClientCredentialsExtensions"/>): sem credenciais nenhum
/// handler é anexado ao pipeline (serve DEV); configuração PARCIAL falha rápido; e duas seções
/// diferentes registradas no mesmo <see cref="IServiceCollection"/> não colidem (serviço
/// keyed por seção).
/// </summary>
public class SeccoClientCredentialsExtensionsTests
{
	private const string SectionKey = "Secco:Teste";
	private const string HttpClientName = "teste-client";

	/// <summary>Responde ao <c>connect/token</c> com um token fixo e captura o Authorization das demais chamadas.</summary>
	private sealed class TokenIssuingHandler : HttpMessageHandler
	{
		public string? CapturedAuthorizationHeader { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("connect/token", StringComparison.Ordinal))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(
						"""{"access_token":"teste-token","expires_in":3600}""", Encoding.UTF8, "application/json"),
				});
			}

			CapturedAuthorizationHeader = request.Headers.Authorization?.ToString();

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{}", Encoding.UTF8, "application/json"),
			});
		}
	}

	private static ServiceProvider BuildProvider(
		IReadOnlyDictionary<string, string?>? configOverride, HttpMessageHandler primaryHandler)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(configOverride ?? new Dictionary<string, string?>())
			.Build();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddSeccoClientCredentialsOptions(SectionKey);

		var tokenStore = new SeccoAccessTokenStore();

		services.AddHttpClient(HttpClientName)
			.ConfigurePrimaryHttpMessageHandler(() => primaryHandler)
			.AddSeccoClientCredentials(
				"teste-scope",
				tokenStore,
				resolve: serviceProvider =>
					serviceProvider.GetRequiredKeyedService<SeccoClientCredentialsOptions>(SectionKey));

		return services.BuildServiceProvider();
	}

	[Fact]
	public async Task SendAsync_WithoutCredentials_DoesNotAttachHandler()
	{
		var handler = new TokenIssuingHandler();
		using var provider = BuildProvider(configOverride: null, handler);

		var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
		client.BaseAddress = new Uri("https://exemplo.test");
		await client.GetAsync("/algum-recurso");

		handler.CapturedAuthorizationHeader.Should().BeNull(
			"sem nenhuma credencial configurada, nenhum handler de autenticação deve ser anexado");
	}

	[Fact]
	public void CreateClient_WithPartialCredentials_FailsFast()
	{
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			// Só o AuthorityUrl: nunca degrada silenciosamente para um client sem autenticação
			[$"{SectionKey}:AuthorityUrl"] = "https://emissor.test",
		}, new TokenIssuingHandler());

		var act = () => provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

		act.Should().Throw<InvalidOperationException>().WithMessage("*parcialmente configurad*");
	}

	[Fact]
	public async Task SendAsync_WithFullCredentials_AttachesBearerToken()
	{
		var handler = new TokenIssuingHandler();
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			[$"{SectionKey}:AuthorityUrl"] = "https://emissor.test",
			[$"{SectionKey}:ClientId"] = "client-teste",
			[$"{SectionKey}:ClientSecret"] = "client-teste-secret",
		}, handler);

		var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
		client.BaseAddress = new Uri("https://exemplo.test");
		await client.GetAsync("/algum-recurso");

		handler.CapturedAuthorizationHeader.Should().Be("Bearer teste-token",
			"o pipeline deve anexar o token emitido pelo handler de client credentials");
	}

	[Fact]
	public void AddSeccoClientCredentialsOptions_TwoDifferentSections_DoNotCollide()
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Secco:ProdutoA:AuthorityUrl"] = "https://produto-a.test",
				["Secco:ProdutoA:ClientId"] = "a",
				["Secco:ProdutoA:ClientSecret"] = "a-secret",
				["Secco:ProdutoB:AuthorityUrl"] = "https://produto-b.test",
				["Secco:ProdutoB:ClientId"] = "b",
				["Secco:ProdutoB:ClientSecret"] = "b-secret",
			})
			.Build();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddSeccoClientCredentialsOptions("Secco:ProdutoA");
		services.AddSeccoClientCredentialsOptions("Secco:ProdutoB");

		using var provider = services.BuildServiceProvider();

		var optionsA = provider.GetRequiredKeyedService<SeccoClientCredentialsOptions>("Secco:ProdutoA");
		var optionsB = provider.GetRequiredKeyedService<SeccoClientCredentialsOptions>("Secco:ProdutoB");

		optionsA.AuthorityUrl.Should().Be("https://produto-a.test");
		optionsB.AuthorityUrl.Should().Be("https://produto-b.test");
	}

	[Fact]
	public void AddSeccoClientCredentialsOptions_OwnSectionMissingKeys_FallsBackToSecureGateSection()
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Secco:SecureGate:BaseUrl"] = "https://securegate.test",
				["Secco:SecureGate:ClientId"] = "fallback-client",
				["Secco:SecureGate:ClientSecret"] = "fallback-secret",
			})
			.Build();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddSeccoClientCredentialsOptions(SectionKey);

		using var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredKeyedService<SeccoClientCredentialsOptions>(SectionKey);

		options.AuthorityUrl.Should().Be("https://securegate.test");
		options.ClientId.Should().Be("fallback-client");
		options.ClientSecret.Should().Be("fallback-secret");
	}
}
