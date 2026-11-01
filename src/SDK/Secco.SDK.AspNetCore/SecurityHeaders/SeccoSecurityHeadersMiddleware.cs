using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Secco.SDK.AspNetCore.SecurityHeaders;

/// <summary>
/// Põe os cabeçalhos de segurança em toda resposta (ADR-0057). Escreve no <c>OnStarting</c>, e não
/// na entrada, porque o tratamento de exceção limpa os cabeçalhos antes de gerar a página de erro.
/// </summary>
internal sealed class SeccoSecurityHeadersMiddleware(RequestDelegate next, SeccoSecurityHeadersPolicy policy, IHostEnvironment environment)
{
	public Task InvokeAsync(HttpContext context)
	{
		// Reexecução do pipeline (UseExceptionHandler, UseStatusCodePagesWithReExecute): o mesmo nonce,
		// senão a página de erro sairia com um nonce e o cabeçalho com outro.
		if (context.Features.Get<SeccoCspNonceFeature>() is null)
		{
			var feature = new SeccoCspNonceFeature(context);
			context.Features.Set(feature);
			context.Response.OnStarting(static state =>
			{
				var (self, httpContext, nonce) = ((SeccoSecurityHeadersMiddleware, HttpContext, SeccoCspNonceFeature))state;
				self.Apply(httpContext, nonce);
				return Task.CompletedTask;
			}, (this, context, feature));
		}

		return next(context);
	}

	private void Apply(HttpContext context, SeccoCspNonceFeature nonce)
	{
		var headers = context.Response.Headers;

		headers.XContentTypeOptions = "nosniff";
		headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
		headers["Permissions-Policy"] = policy.PermissionsPolicy;
		headers[policy.CspHeaderName] = policy.ContentSecurityPolicy(nonce.Value);

		// Sobrescreve o SAMEORIGIN que o antiforgery põe por conta própria, para ficar coerente com frame-ancestors.
		if (policy.XFrameOptions is { } frameOptions)
		{
			headers.XFrameOptions = frameOptions;
		}

		// Sai também por HTTP: o navegador o ignora sem TLS (RFC 6797), e assim chega atrás de proxy que termina o TLS.
		if (policy.StrictTransportSecurity is { } hsts && !environment.IsDevelopment() && !IsLoopback(context.Request.Host.Host))
		{
			headers.StrictTransportSecurity = hsts;
		}

		if (nonce.Used)
		{
			headers.CacheControl = "no-store";
		}
	}

	private static bool IsLoopback(string host)
		=> string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
			|| (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}
