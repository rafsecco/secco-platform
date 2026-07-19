using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Composição de DI do <see cref="ISecureGateClient"/> administrativo
/// (<see cref="SecureGateAdminClientExtensions.AddSecureGateAdminClient"/>): sem a seção
/// <c>Secco:SecureGate</c> o client fica sem autenticação e falha ao primeiro uso;
/// configuração parcial nunca é ignorada silenciosamente (fail-fast, ADR-0020); e, com a
/// seção completa, o pipeline anexa o Bearer emitido pelo handler de client credentials.
/// </summary>
public class SecureGateAdminClientExtensionsTests
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
                        """{"access_token":"admin-token","expires_in":3600}""", Encoding.UTF8, "application/json"),
                });
            }

            CapturedAuthorizationHeader = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
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
        services.AddLogging();
        services.AddSecureGateAdminClient();

        // Sobrepõe apenas o handler primário (mesmo nome do client tipado gerado por
        // AddHttpClient<TClient, TImplementation>) — o handler de client credentials
        // anexado por AddSecureGateAdminClient continua no pipeline, como no
        // SecureGateCatalogClientTests (ConfigurePrimaryHttpMessageHandler pós-registro).
        services.AddHttpClient<ISecureGateClient, SecureGateClient>()
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ListTenantsAsync_WithoutConfiguration_FailsWithoutBaseAddress()
    {
        using var provider = BuildProvider(configOverride: null, new TokenIssuingHandler());
        var client = provider.GetRequiredService<ISecureGateClient>();

        var act = async () => await client.ListTenantsAsync(CancellationToken.None);

        // Sem a seção Secco:SecureGate, nenhum handler de autenticação é anexado nem a
        // BaseAddress é definida — o client nunca funciona "sem querer" e sem token
        await act.Should().ThrowAsync<InvalidOperationException>(
            "sem BaseAddress a requisição não tem para onde ir — nunca silenciosamente sem auth");
    }

    [Fact]
    public void GetRequiredService_WithPartialConfiguration_FailsFast()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            // Só a BaseUrl: nunca degrada silenciosamente para um client sem autenticação
            ["Secco:SecureGate:BaseUrl"] = "https://securegate.test",
        }, new TokenIssuingHandler());

        var act = () => provider.GetRequiredService<ISecureGateClient>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*parcialmente configurad*");
    }

    [Fact]
    public async Task ListTenantsAsync_WithFullConfiguration_AttachesBearerFromClientCredentials()
    {
        var handler = new TokenIssuingHandler();
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Secco:SecureGate:BaseUrl"] = "https://securegate.test",
            ["Secco:SecureGate:ClientId"] = "admin-client-tests",
            ["Secco:SecureGate:ClientSecret"] = "admin-client-tests-secret",
        }, handler);

        var client = provider.GetRequiredService<ISecureGateClient>();
        await client.ListTenantsAsync(CancellationToken.None);

        handler.CapturedAuthorizationHeader.Should().Be("Bearer admin-token",
            "o pipeline do client administrativo anexa o token do handler de client credentials");
    }
}
