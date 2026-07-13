using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Converte as exceções de tenancy em ProblemDetails (ADR-0004): sem tenant resolvido ou
/// tenant desconhecido são erro do chamador (400); catálogo indisponível é condição
/// transitória (503 + <c>Retry-After</c> — o cliente com retry da plataforma se recupera
/// sozinho). Cirúrgico por design: qualquer outra exceção segue o fluxo normal — este
/// middleware não substitui um exception handler global.
/// </summary>
public sealed class SeccoTenancyExceptionMiddleware(RequestDelegate next)
{
    /// <summary>Segundos sugeridos no <c>Retry-After</c> quando o catálogo está indisponível.</summary>
    internal const int RetryAfterSeconds = 15;

    /// <summary>Invoca o próximo delegate traduzindo exceções de tenancy conhecidas.</summary>
    /// <param name="context">Contexto HTTP da requisição atual.</param>
    /// <param name="logger">Logger para sinalizar catálogo indisponível.</param>
    public async Task InvokeAsync(HttpContext context, ILogger<SeccoTenancyExceptionMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (TenantNotResolvedException) when (!context.Response.HasStarted)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                "Tenant não resolvido",
                "A requisição exige um tenant (claim tenant_id ou header X-Tenant-Id).").ConfigureAwait(false);
        }
        catch (TenantNotFoundException) when (!context.Response.HasStarted)
        {
            // O detalhe não ecoa o identificador recebido (ADR-0020)
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                "Tenant desconhecido",
                "O tenant da requisição não existe no catálogo da plataforma.").ConfigureAwait(false);
        }
        catch (TenantCatalogUnavailableException exception) when (!context.Response.HasStarted)
        {
            TenancyLog.CatalogUnavailable(logger, exception);

            context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable,
                "Catálogo de tenants indisponível",
                "O catálogo de tenants está temporariamente indisponível. Tente novamente.").ConfigureAwait(false);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }
}
