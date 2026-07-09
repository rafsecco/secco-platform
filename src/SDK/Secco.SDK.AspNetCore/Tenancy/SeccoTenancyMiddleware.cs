using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Secco.SharedKernel.Constants;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Resolve o tenant da requisição (ADR-0005): claim <c>tenant_id</c> como fonte primária,
/// header <c>X-Tenant-Id</c> apenas na ausência de claim, divergência entre os dois rejeita
/// a requisição com 400 (ADR-0020). Não bloqueia requisições sem tenant — endpoints públicos
/// e health checks funcionam; o isolamento é garantido pelo <see cref="ITenantConnectionFactory"/>.
/// Deve ser registrado <b>após</b> o middleware de autenticação, que popula as claims.
/// </summary>
public sealed class SeccoTenancyMiddleware(RequestDelegate next)
{
    /// <summary>Executa a resolução de tenant e invoca o próximo delegate do pipeline.</summary>
    /// <param name="context">Contexto HTTP da requisição atual.</param>
    /// <param name="tenantContext">Contexto de tenant do escopo (injetado pelo DI).</param>
    /// <param name="logger">Logger para sinalizar conflitos de tenant.</param>
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext, ILogger<SeccoTenancyMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);

        var claimValue = context.User.FindFirst(SeccoClaims.TenantId)?.Value;
        var headerValue = context.Request.Headers[SeccoHeaders.TenantId].ToString();

        var resolution = TenantResolver.Resolve(claimValue, headerValue);

        if (resolution.IsConflict)
        {
            // ADR-0020: não logar o valor bruto do header (log forging) — só o fato do conflito.
            TenancyLog.TenantConflict(logger);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Conflito de tenant",
                    Detail = "O header X-Tenant-Id diverge do tenant do token de acesso.",
                },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted).ConfigureAwait(false);

            return;
        }

        tenantContext.TenantId = resolution.TenantId;

        await next(context).ConfigureAwait(false);
    }
}
