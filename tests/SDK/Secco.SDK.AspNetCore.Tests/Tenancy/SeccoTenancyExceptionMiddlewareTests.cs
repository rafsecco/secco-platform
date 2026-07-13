using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Tenancy;

/// <summary>
/// Exceções de tenancy → ProblemDetails (Fase 6.3): erro do chamador vira 400 e catálogo
/// indisponível vira 503 + Retry-After; qualquer outra exceção segue intocada.
/// </summary>
public class SeccoTenancyExceptionMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(RequestDelegate next)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new SeccoTenancyExceptionMiddleware(next);
        await middleware.InvokeAsync(context, NullLogger<SeccoTenancyExceptionMiddleware>.Instance);

        return context;
    }

    private static JsonElement ReadProblem(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;

        using var reader = new StreamReader(context.Response.Body);

        return JsonSerializer.Deserialize<JsonElement>(reader.ReadToEnd());
    }

    [Fact]
    public async Task Invoke_WithTenantNotResolved_Returns400ProblemDetails()
    {
        var context = await InvokeAsync(_ => throw new TenantNotResolvedException());

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        ReadProblem(context).GetProperty("title").GetString().Should().Be("Tenant não resolvido");
    }

    [Fact]
    public async Task Invoke_WithTenantNotFound_Returns400WithoutEchoingTenantId()
    {
        var tenantId = Guid.NewGuid();
        var context = await InvokeAsync(_ => throw new TenantNotFoundException(tenantId));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().NotContain(tenantId.ToString(), "o detalhe não ecoa o identificador recebido (ADR-0020)");
    }

    [Fact]
    public async Task Invoke_WithCatalogUnavailable_Returns503WithRetryAfter()
    {
        var context = await InvokeAsync(_ => throw new TenantCatalogUnavailableException());

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("15");
        ReadProblem(context).GetProperty("title").GetString().Should().Be("Catálogo de tenants indisponível");
    }

    [Fact]
    public async Task Invoke_WithUnrelatedException_Propagates()
    {
        var act = async () => await InvokeAsync(_ => throw new InvalidOperationException("bug real"));

        // Cirúrgico por design: este middleware não é um exception handler global
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Invoke_WithSuccessfulPipeline_PassesThrough()
    {
        var context = await InvokeAsync(httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status204NoContent;

            return Task.CompletedTask;
        });

        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }
}
