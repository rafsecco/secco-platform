using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Client;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

/// <summary>
/// Composição de DI do client (<see cref="LogStreamClientServiceCollectionExtensions.AddLogStreamClient"/>,
/// issue #25): sem credenciais o pipeline segue sem handler de autenticação (serve DEV);
/// configuração parcial falha rápido (ADR-0020); com credenciais completas, o Bearer emitido
/// pelo client credentials é anexado a cada chamada; e o scope, quando não informado, é o
/// padrão do produto.
/// </summary>
public class LogStreamClientServiceCollectionExtensionsTests
{
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
						"""{"access_token":"logstream-token","expires_in":3600}""", Encoding.UTF8, "application/json"),
				});
			}

			CapturedAuthorizationHeader = request.Headers.Authorization?.ToString();

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json"),
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
		services.AddLogStreamClient(o => o.BaseUrl = "https://logstream.test");

		// Sobrepõe apenas o handler primário (mesmo nome do client tipado gerado por
		// AddHttpClient<TClient, TImplementation>) — o handler de client credentials anexado
		// por AddLogStreamClient continua no pipeline.
		services.AddHttpClient<ILogStreamClient, LogStreamClient>()
			.ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

		return services.BuildServiceProvider();
	}

	[Fact]
	public void DefaultScope_IsLogStream()
	{
		LogStreamClientOptions.DefaultScope.Should().Be("logstream");
	}

	[Fact]
	public void Scope_NotOverridden_DefaultsToProductScope()
	{
		var options = new LogStreamClientOptions();

		options.Scope.Should().Be("logstream");
	}

	[Fact]
	public async Task SearchLogEntriesAsync_WithoutCredentials_DoesNotAttachHandler()
	{
		var handler = new TokenIssuingHandler();
		using var provider = BuildProvider(configOverride: null, handler);

		var client = provider.GetRequiredService<ILogStreamClient>();
		await client.SearchLogEntriesAsync(null, null, null, null, null, null, null, null, null);

		handler.CapturedAuthorizationHeader.Should().BeNull(
			"sem nenhuma credencial configurada, nenhum handler de autenticação deve ser anexado");
	}

	[Fact]
	public void GetRequiredService_WithPartialCredentials_FailsFast()
	{
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			// Só o AuthorityUrl: nunca degrada silenciosamente para um client sem autenticação
			["Secco:LogStream:AuthorityUrl"] = "https://securegate.test",
		}, new TokenIssuingHandler());

		var act = () => provider.GetRequiredService<ILogStreamClient>();

		act.Should().Throw<InvalidOperationException>().WithMessage("*parcialmente configurad*");
	}

	[Fact]
	public async Task SearchLogEntriesAsync_WithFullCredentials_AttachesBearerToken()
	{
		var handler = new TokenIssuingHandler();
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			["Secco:LogStream:AuthorityUrl"] = "https://securegate.test",
			["Secco:LogStream:ClientId"] = "logstream-client-tests",
			["Secco:LogStream:ClientSecret"] = "logstream-client-tests-secret",
		}, handler);

		var client = provider.GetRequiredService<ILogStreamClient>();
		await client.SearchLogEntriesAsync(null, null, null, null, null, null, null, null, null);

		handler.CapturedAuthorizationHeader.Should().Be("Bearer logstream-token",
			"o pipeline do client deve anexar o token emitido pelo handler de client credentials");
	}
}
