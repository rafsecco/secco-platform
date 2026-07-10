using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Extensions;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Resilience;

public class SeccoResilienceTests
{
    /// <summary>Handler primário fake: devolve a sequência de status configurada, contando as tentativas.</summary>
    private sealed class SequenceHandler(params HttpStatusCode[] sequence) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = sequence[Math.Min(Attempts, sequence.Length - 1)];
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
        }
    }

    private static HttpClient CreateClient(SequenceHandler handler)
    {
        var services = new ServiceCollection();

        // Delays mínimos para o teste não esperar o backoff exponencial real
        services.AddSeccoResilience(options =>
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        });

        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("test");
        client.BaseAddress = new Uri("http://localhost/");
        return client;
    }

    [Fact]
    public async Task Get_WithTransientFailures_RetriesUntilSuccess()
    {
        var handler = new SequenceHandler(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK);
        using var client = CreateClient(handler);

        var response = await client.GetAsync("/recurso");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Post_WithTransientFailure_DoesNotRetry()
    {
        var handler = new SequenceHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        using var client = CreateClient(handler);

        var response = await client.PostAsync("/recurso", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        handler.Attempts.Should().Be(1, "repetir POST após falha pode duplicar o efeito no servidor");
    }

    [Fact]
    public async Task Delete_WithTransientFailure_Retries()
    {
        var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.NoContent);
        using var client = CreateClient(handler);

        var response = await client.DeleteAsync("/recurso/1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        handler.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Get_WithNonTransientResponse_DoesNotRetry()
    {
        var handler = new SequenceHandler(HttpStatusCode.NotFound);
        using var client = CreateClient(handler);

        var response = await client.GetAsync("/recurso");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        handler.Attempts.Should().Be(1, "404 não é falha transiente");
    }

    [Fact]
    public async Task Get_WhenAllRetriesFail_ReturnsLastResponse()
    {
        var handler = new SequenceHandler(HttpStatusCode.InternalServerError);
        using var client = CreateClient(handler);

        var response = await client.GetAsync("/recurso");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        handler.Attempts.Should().Be(4, "1 tentativa original + 3 retries do pipeline padrão");
    }
}
