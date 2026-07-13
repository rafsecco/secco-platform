using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

public class ClientCredentialsFlowTests(SecureGateApiFactory factory) : IClassFixture<SecureGateApiFactory>, IAsyncLifetime
{
    private const string ClientId = "test-client";
    private const string ClientSecret = "test-client-secret-de-32-chars-min!!";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        await factory.EnsureDatabaseMigratedAsync();
        await factory.CreateClientAsync(ClientId, ClientSecret, "logstream");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpResponseMessage> RequestTokenAsync(string clientId, string secret, string scope) =>
        await factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = scope,
        }));

    [Fact]
    public async Task Discovery_Always_ExposesTokenEndpointAndJwks()
    {
        var discovery = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", Json);

        discovery.GetProperty("token_endpoint").GetString().Should().EndWith("/connect/token");
        discovery.GetProperty("jwks_uri").GetString().Should().NotBeNullOrEmpty();
        discovery.GetProperty("grant_types_supported").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("client_credentials");
    }

    [Fact]
    public async Task Jwks_Always_ExposesSigningKeys()
    {
        var discovery = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", Json);
        var jwksUri = new Uri(discovery.GetProperty("jwks_uri").GetString()!);

        var jwks = await factory.CreateClient().GetFromJsonAsync<JsonElement>(jwksUri.PathAndQuery, Json);

        jwks.GetProperty("keys").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ClientCredentials_WithValidClient_IssuesJwtWithShortClaims()
    {
        var response = await RequestTokenAsync(ClientId, ClientSecret, "logstream");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        payload.GetProperty("token_type").GetString().Should().Be("Bearer");

        // JWT puro (DisableAccessTokenEncryption): legível por qualquer validador padrão
        var token = new JsonWebTokenHandler().ReadJsonWebToken(payload.GetProperty("access_token").GetString());

        token.GetClaim("sub").Value.Should().Be(ClientId, "claims curtas da ADR-0007");
        token.Audiences.Should().Contain("secco-logstream", "o scope logstream mapeia para a audience do produto");
        token.Claims.Should().Contain(c => c.Type == "scope" && c.Value == "logstream",
            "a claim curta 'scope' da ADR-0007 sai no JWT final");
    }

    [Fact]
    public async Task ClientCredentials_WithWrongSecret_ReturnsInvalidClient()
    {
        var response = await RequestTokenAsync(ClientId, "segredo-errado-mas-com-32-chars!!!!", "logstream");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }

    [Fact]
    public async Task ClientCredentials_WithUnauthorizedScope_ReturnsError()
    {
        var response = await RequestTokenAsync(ClientId, ClientSecret, "securegate");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "o client de teste só tem permissão para o scope logstream");
    }
}
