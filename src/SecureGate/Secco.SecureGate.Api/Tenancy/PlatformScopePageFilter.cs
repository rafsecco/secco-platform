using Microsoft.AspNetCore.Mvc.Filters;
using Secco.SecureGate.Application.Tenancy;

namespace Secco.SecureGate.Api.Tenancy;

/// <summary>
/// Telas de login e de conta da própria pessoa (<c>/login</c>, <c>/login/dois-fatores</c>,
/// <c>/conta/*</c>) rodam em modo plataforma (ADR-0041): quem as usa é achado pelo e-mail do
/// formulário ou pela sessão, e a pessoa atravessa tenants (ADR-0040) — não há tenant de operação.
/// Um ponto só para todas as páginas, e uma linha na allowlist.
/// </summary>
public sealed class PlatformScopePageFilter : IAsyncPageFilter
{
	/// <inheritdoc />
	public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

	/// <inheritdoc />
	public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		var scope = context.HttpContext.RequestServices.GetRequiredService<ITenantAccessScope>();

		using (scope.EnterPlatform("telas de login e conta: a pessoa é achada por e-mail ou sessão, sem tenant de operação"))
		{
			await next();
		}
	}
}
