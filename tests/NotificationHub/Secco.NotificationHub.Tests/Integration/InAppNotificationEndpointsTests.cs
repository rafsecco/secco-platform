using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Ponta a ponta real do inbox in-app (Fase 8.4): Testcontainers SQL Server (ADR-0012),
/// sem Hangfire envolvido — a "entrega" in-app é a própria escrita no banco do tenant.
/// </summary>
[Collection(NotificationHubApiCollectionDefinition.Name)]
public class InAppNotificationEndpointsTests(NotificationHubApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientForTenant(Guid tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", NotificationHubApiFactory.CreateToken(tenantId));
        return client;
    }

    private static async Task<Guid> DispatchInAppAsync(HttpClient client, Guid userId, string title = "Título")
    {
        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            userId,
            title,
            message = "Mensagem",
            source = "secco-intranet",
            type = "aviso",
            link = "/pagina",
            channels = new[] { "in_app" },
        });

        response.EnsureSuccessStatusCode();
        var dispatched = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return dispatched.GetProperty("inAppNotificationId").GetGuid();
    }

    [Fact]
    public async Task GetUnread_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"/api/v1/in-app-notifications?userId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUnread_AfterDispatch_ReturnsTheItem()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);
        var userId = Guid.NewGuid();
        await DispatchInAppAsync(client, userId, "Aviso importante");

        var unread = await client.GetFromJsonAsync<JsonElement>($"/api/v1/in-app-notifications?userId={userId}", Json);

        unread.GetArrayLength().Should().Be(1);
        unread[0].GetProperty("title").GetString().Should().Be("Aviso importante");
        unread[0].GetProperty("isRead").GetBoolean().Should().BeFalse();
        unread[0].GetProperty("link").GetString().Should().Be("/pagina");
    }

    [Fact]
    public async Task GetUnreadCount_ReflectsOnlyUnreadItemsOfThatUser()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        await DispatchInAppAsync(client, userId);
        await DispatchInAppAsync(client, userId);
        await DispatchInAppAsync(client, otherUserId);

        var count = await client.GetFromJsonAsync<JsonElement>($"/api/v1/in-app-notifications/count?userId={userId}", Json);

        count.GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task MarkAsRead_RemovesItemFromUnreadListAndCount()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);
        var userId = Guid.NewGuid();
        var id = await DispatchInAppAsync(client, userId);

        var readResponse = await client.PostAsync($"/api/v1/in-app-notifications/{id}/read", content: null);
        readResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var count = await client.GetFromJsonAsync<JsonElement>($"/api/v1/in-app-notifications/count?userId={userId}", Json);
        count.GetInt32().Should().Be(0);

        var unread = await client.GetFromJsonAsync<JsonElement>($"/api/v1/in-app-notifications?userId={userId}", Json);
        unread.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MarkAsRead_WithUnknownId_Returns404()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsync($"/api/v1/in-app-notifications/{Guid.NewGuid()}/read", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUnread_FromAnotherTenant_NeverSeesTheItem()
    {
        var clientAlfa = CreateClientForTenant(factory.TenantAlfa);
        var clientBeta = CreateClientForTenant(factory.TenantBeta);
        var userId = Guid.NewGuid();

        await DispatchInAppAsync(clientAlfa, userId, "segredo do alfa");

        var unreadFromBeta = await clientBeta.GetFromJsonAsync<JsonElement>($"/api/v1/in-app-notifications?userId={userId}", Json);

        unreadFromBeta.GetArrayLength().Should().Be(0, "cada tenant possui banco próprio (ADR-0005)");
    }
}
