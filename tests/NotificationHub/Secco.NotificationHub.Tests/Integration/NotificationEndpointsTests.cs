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
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/notifications", new { recipient = "sem-token@test.local", subject = "x", body = "y" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a FallbackPolicy protege endpoints sem metadata explícita (ADR-0020)");
    }

    [Fact]
    public async Task PostNotification_WithoutRecipient_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new { recipient = "", subject = "assunto", body = "corpo" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NotificationHub.Notification.RecipientRequired");
    }

    [Fact]
    public async Task PostNotification_WithInvalidRecipientFormat_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new { recipient = "não-é-um-email", subject = "assunto", body = "corpo" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NotificationHub.Notification.RecipientInvalid");
    }

    [Fact]
    public async Task PostNotification_WithValidRequest_Returns201AndEventuallySent()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipient = "destinatario@notificationhub.test",
            subject = "Bem-vindo",
            body = "Corpo da mensagem",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        created.GetProperty("status").GetString().Should().Be("Pending");
        var id = created.GetProperty("id").GetGuid();

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
            subject = "Assunto",
            body = "Corpo",
        });

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

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
            subject = "assunto",
            body = "corpo",
        });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        (await clientBeta.GetAsync($"/api/v1/notifications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "cada tenant possui banco próprio (ADR-0005)");
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
