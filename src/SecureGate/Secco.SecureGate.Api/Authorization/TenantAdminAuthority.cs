using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Authorization;

/// <summary>
/// Marca uma rota como aceitando a autoridade de admin de tenant (ADR-0044). Sem esta marca, o
/// escopo <c>securegate:tenant-admin</c> não abre a rota, mesmo num grupo que o admite.
/// </summary>
/// <param name="ProtectsAdminTargets">
/// Operação de pessoa, ou que revoga as sessões dela: recusa quando a pessoa alvo (<c>{userId}</c>) é conta
/// administrativa — tem concessão de admin de tenant ou é operador. Senão o admin só de A enfraqueceria a
/// conta de quem administra A e B, ou derrubaria em ciclo as sessões do operador (incluí-lo como membro e
/// suspender o vínculo move o stamp dele, ADR-0032).
/// </param>
public sealed record TenantAdminAllowedMetadata(bool ProtectsAdminTargets);

/// <summary>
/// Autoridade de admin de tenant (ADR-0044): negada por padrão, aberta rota a rota, conferida no
/// cadastro a cada requisição e auditada de forma síncrona e fail-closed antes de toda escrita.
/// </summary>
internal static class TenantAdminAuthority
{
	/// <summary>
	/// Policy dos grupos administrativos que têm rotas abertas a admin de tenant: o operador
	/// (<c>securegate:admin</c>) passa como sempre; <c>securegate:tenant-admin</c> só passa em rota
	/// MARCADA — rota nova num destes grupos nasce fechada a ele.
	/// </summary>
	/// <param name="context">Contexto da autorização (o recurso é o <see cref="HttpContext"/>).</param>
	public static bool AllowsAdminOrMarkedTenantAdmin(AuthorizationHandlerContext context) =>
		ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)
		|| (ScopeAuthorization.HasScope(context.User, SecureGateScopes.TenantAdmin)
			&& context.Resource is HttpContext httpContext
			&& httpContext.GetEndpoint()?.Metadata.GetMetadata<TenantAdminAllowedMetadata>() is not null);

	/// <summary>Abre a rota à autoridade de admin de tenant: marca + filtro que confere e audita.</summary>
	/// <param name="builder">Rota.</param>
	/// <param name="protectsAdminTargets">Ver <see cref="TenantAdminAllowedMetadata.ProtectsAdminTargets"/>.</param>
	public static RouteHandlerBuilder AllowTenantAdmin(this RouteHandlerBuilder builder, bool protectsAdminTargets = false)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder
			.WithMetadata(new TenantAdminAllowedMetadata(protectsAdminTargets))
			.AddEndpointFilter(async (context, next) =>
				await AuthorizeAsync(context.HttpContext, protectsAdminTargets).ConfigureAwait(false) ?? await next(context));
	}

	/// <summary>Recusa padrão: não diz se o tenant existe nem por que a autoridade não vale.</summary>
	public static IResult Forbidden() =>
		Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Acesso negado.");

	// null = segue para o handler; qualquer outro valor é a resposta
	private static async Task<IResult?> AuthorizeAsync(HttpContext httpContext, bool protectsAdminTargets)
	{
		var principal = httpContext.User;

		// O operador segue exatamente como antes desta ADR: sem conferência de concessão, sem esta trilha
		if (ScopeAuthorization.HasScope(principal, SecureGateScopes.Admin))
		{
			return null;
		}

		var services = httpContext.RequestServices;
		var auditor = services.GetRequiredService<ITenantAdminAuditor>();

		if (!auditor.IsConfigured
			|| !Guid.TryParse(principal.FindFirstValue(SeccoClaims.Subject), out var userId)
			|| !TryGetRouteGuid(httpContext, out var tenantId, "tenantId", "id"))
		{
			return Forbidden();
		}

		var grants = services.GetRequiredService<ITenantAdminGrantRepository>();
		var cancellationToken = httpContext.RequestAborted;

		if (tenantId == SecureGatePlatform.TenantId
			|| !await grants.HasGrantAsync(tenantId, userId, cancellationToken).ConfigureAwait(false)
			|| !await CanStillActAsync(services, userId, cancellationToken).ConfigureAwait(false))
		{
			return Forbidden();
		}

		Guid? targetUserId = TryGetRouteGuid(httpContext, out var target, "userId") ? target : null;

		// Rota protegida sem alvo legível é erro de mapeamento: recusa em vez de seguir sem conferir
		if (protectsAdminTargets
			&& (targetUserId is not { } protectedTarget
				|| await grants.IsAdministrativeAccountAsync(protectedTarget, cancellationToken).ConfigureAwait(false)))
		{
			return Forbidden();
		}

		if (HttpMethods.IsGet(httpContext.Request.Method))
		{
			return null;
		}

		var operation = httpContext.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "desconhecida";
		var recorded = await auditor.RecordAsync(
			new TenantAdminAuditRecord(
				userId,
				principal.FindFirstValue("name"),
				tenantId,
				principal.FindFirstValue("client_id") ?? principal.FindFirstValue("azp"),
				operation,
				targetUserId),
			cancellationToken).ConfigureAwait(false);

		return recorded
			? null
			: Results.Problem(
				statusCode: StatusCodes.Status503ServiceUnavailable,
				title: "Trilha de auditoria indisponível: a operação não foi feita.");
	}

	// Estado ATUAL da conta (ADR-0032): o token pode ter sido emitido antes de a conta ser desativada
	private static async Task<bool> CanStillActAsync(IServiceProvider services, Guid userId, CancellationToken cancellationToken)
	{
		using (services.GetRequiredService<ITenantAccessScope>()
			.EnterPlatform("admin de tenant: estado da conta de quem age, dona de qualquer tenant"))
		{
			var userManager = services.GetRequiredService<UserManager<User>>();
			var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

			return user is not null
				&& await AccountStateGuard.CanReceiveTokensAsync(
					user,
					userManager,
					services.GetRequiredService<SignInManager<User>>(),
					services.GetRequiredService<ITenantRepository>(),
					cancellationToken).ConfigureAwait(false);
		}
	}

	private static bool TryGetRouteGuid(HttpContext httpContext, out Guid value, params string[] names)
	{
		foreach (var name in names)
		{
			if (Guid.TryParse(httpContext.GetRouteValue(name)?.ToString(), out value) && value != Guid.Empty)
			{
				return true;
			}
		}

		value = Guid.Empty;

		return false;
	}
}
