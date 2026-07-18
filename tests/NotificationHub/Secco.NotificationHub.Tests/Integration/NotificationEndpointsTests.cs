using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Ponta a ponta real: Testcontainers SQL Server (bancos de tenant + banco de plataforma
/// do Hangfire, ADR-0015) e o job de envio de fato processado pelo Hangfire — só o envio
/// de e-mail em si é fake (<see cref="FakeEmailSender"/>, sem SMTP real).
/// </summary>
[Collection(NotificationHubApiCollectionDefinition.Name)]
public class NotificationEndpointsTests(NotificationHubApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(20);

    public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientForTenant(Guid tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", NotificationHubApiFactory.CreateToken(tenantId));
        return client;
    }

    [Fact]
    public async Task HealthEndpoints_Always_RespondAnonymously()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostNotification_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "sem-token@test.local",
            title = "x",
            message = "y",
            channels = new[] { "email" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a FallbackPolicy protege endpoints sem metadata explícita (ADR-0020)");
    }

    [Fact]
    public async Task PostNotification_WithoutChannels_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "destinatario@notificationhub.test",
            title = "assunto",
            message = "corpo",
            channels = Array.Empty<string>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NotificationHub.Notification.ChannelsRequired");
    }

    [Fact]
    public async Task PostNotification_WithUnsupportedChannel_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "destinatario@notificationhub.test",
            title = "assunto",
            message = "corpo",
            channels = new[] { "sms" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NotificationHub.Notification.ChannelUnsupported");
    }

    [Fact]
    public async Task PostNotification_WithEmailChannelAndNoRecipient_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "",
            title = "assunto",
            message = "corpo",
            channels = new[] { "email" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NotificationHub.Notification.RecipientRequired");
    }

    [Fact]
    public async Task PostNotification_WithInvalidRecipientFormat_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "não-é-um-email",
            title = "assunto",
            message = "corpo",
            channels = new[] { "email" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NotificationHub.Notification.RecipientInvalid");
    }

    [Fact]
    public async Task PostNotification_WithEmailChannel_Returns202AndEventuallySent()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "destinatario@notificationhub.test",
            title = "Bem-vindo",
            message = "Corpo da mensagem",
            channels = new[] { "email" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var dispatched = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        dispatched.GetProperty("inAppNotificationId").ValueKind.Should().Be(JsonValueKind.Null);
        var id = dispatched.GetProperty("emailNotificationId").GetGuid();

        var final = await PollUntilStatusIsNot(client, id, "Pending");

        final.GetProperty("status").GetString().Should().Be("Sent",
            "o job real do Hangfire processa a fila e o FakeEmailSender aceita este destinatário");
        final.GetProperty("sentAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task PostNotification_ToAlwaysFailingRecipient_EventuallyMarkedFailedWithReason()
    {
        var client = CreateClientForTenant(factory.TenantBeta);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = FakeEmailSender.AlwaysFailingRecipient,
            title = "Assunto",
            message = "Corpo",
            channels = new[] { "email" },
        });

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("emailNotificationId").GetGuid();

        var final = await PollUntilStatusIsNot(client, id, "Pending");

        final.GetProperty("status").GetString().Should().Be("Failed");
        final.GetProperty("failureReason").GetString().Should().Contain("Falha de envio simulada");
    }

    [Fact]
    public async Task GetNotification_FromAnotherTenant_Returns404BecauseDatabasesAreIsolated()
    {
        var clientAlfa = CreateClientForTenant(factory.TenantAlfa);
        var clientBeta = CreateClientForTenant(factory.TenantBeta);

        var response = await clientAlfa.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "segredo-do-alfa@notificationhub.test",
            title = "assunto",
            message = "corpo",
            channels = new[] { "email" },
        });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("emailNotificationId").GetGuid();

        (await clientBeta.GetAsync($"/api/v1/notifications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "cada tenant possui banco próprio (ADR-0005)");
    }

    [Fact]
    public async Task PostNotification_WithBothChannels_CreatesEmailAndInAppFromOneCall()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);
        var userId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            userId,
            recipient = "destinatario@notificationhub.test",
            title = "Bem-vindo",
            message = "Corpo da mensagem",
            source = "secco-intranet",
            type = "boas-vindas",
            channels = new[] { "email", "in_app" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var dispatched = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        dispatched.GetProperty("emailNotificationId").ValueKind.Should().Be(JsonValueKind.String);
        dispatched.GetProperty("inAppNotificationId").ValueKind.Should().Be(JsonValueKind.String);

        var unread = await client.GetFromJsonAsync<JsonElement>($"/api/v1/in-app-notifications?userId={userId}", Json);
        unread.GetArrayLength().Should().Be(1);
        unread[0].GetProperty("title").GetString().Should().Be("Bem-vindo");
    }

    private static async Task<JsonElement> PollUntilStatusIsNot(HttpClient client, Guid id, string transientStatus)
    {
        var deadline = DateTime.UtcNow + PollTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var current = await client.GetFromJsonAsync<JsonElement>($"/api/v1/notifications/{id}", Json);

            if (current.GetProperty("status").GetString() != transientStatus)
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }

        throw new TimeoutException($"A notificação {id} continuou '{transientStatus}' após {PollTimeout}.");
    }
}
