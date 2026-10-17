using Secco.SecureGate.Application.Tenancy;

namespace Secco.SecureGate.Api.Tenancy;

/// <summary>
/// Entra no escopo do tenant da ROTA antes do handler (ADR-0041) e o mantém até o fim dele. O valor
/// vem do roteamento, que já o validou como Guid (<c>{tenantId:guid}</c>) — nunca de corpo, query
/// string ou header. Quem prova que o tenant existe segue sendo o handler.
/// </summary>
/// <param name="routeValueName">Nome do valor de rota que carrega o tenant (<c>tenantId</c> ou <c>id</c>).</param>
public sealed class TenantScopeEndpointFilter(string routeValueName) : IEndpointFilter
{
	/// <inheritdoc />
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		var httpContext = context.HttpContext;

		// Rota sem o valor ou com Guid vazio é erro de mapeamento ou de requisição: nunca entra num
		// escopo "qualquer". O 404 é o mesmo que o handler daria a um tenant desconhecido.
		if (!Guid.TryParse(httpContext.GetRouteValue(routeValueName)?.ToString(), out var tenantId)
			|| tenantId == Guid.Empty)
		{
			return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Tenant não encontrado.");
		}

		var scope = httpContext.RequestServices.GetRequiredService<ITenantAccessScope>();

		using (scope.EnterTenant(tenantId))
		{
			return await next(context);
		}
	}
}

/// <summary>Atalho de mapeamento para o <see cref="TenantScopeEndpointFilter"/>.</summary>
public static class TenantScopeEndpointExtensions
{
	/// <summary>
	/// Faz o grupo ou a rota rodar no escopo do tenant da rota (ADR-0041).
	/// </summary>
	/// <param name="builder">Grupo ou rota.</param>
	/// <param name="routeValueName">Valor de rota que carrega o tenant (<c>tenantId</c> por padrão).</param>
	public static TBuilder RequireTenantScope<TBuilder>(this TBuilder builder, string routeValueName = "tenantId")
		where TBuilder : IEndpointConventionBuilder =>
		builder.AddEndpointFilter(new TenantScopeEndpointFilter(routeValueName));

	/// <summary>
	/// Faz a rota ou o grupo rodar em modo plataforma (ADR-0041), para caminhos em que o tenant não
	/// existe. A entrada é escrita NO PONTO DE USO — <c>scope =&gt; scope.EnterPlatform("motivo")</c> —
	/// de propósito: cada ocorrência de <c>EnterPlatform(</c> é uma linha da allowlist versionada
	/// e o motivo fica ao lado da rota que o pede.
	/// </summary>
	/// <param name="builder">Grupo ou rota.</param>
	/// <param name="enter">Entrada no modo plataforma, com o motivo.</param>
	public static TBuilder RequirePlatformScope<TBuilder>(this TBuilder builder, Func<ITenantAccessScope, IDisposable> enter)
		where TBuilder : IEndpointConventionBuilder
	{
		ArgumentNullException.ThrowIfNull(enter);

		return builder.AddEndpointFilter(async (context, next) =>
		{
			var scope = context.HttpContext.RequestServices.GetRequiredService<ITenantAccessScope>();

			using (enter(scope))
			{
				return await next(context);
			}
		});
	}
}
