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
/// administrativa — tem concessão de admin de tenant ou de criação, ou é operador. Senão o admin só de A
/// enfraqueceria a conta de quem administra A e B, ou derrubaria em ciclo as sessões do operador (incluí-lo
/// como membro e suspender o vínculo move o stamp dele, ADR-0032).
/// </param>
/// <param name="CreatesTenant">
/// A rota cria tenant (ADR-0047): não há tenant na rota, e a autoridade vem da concessão de criação.
/// </param>
/// <param name="AnyGrant">
/// Leitura sem tenant na rota (ADR-0048, catálogo de permissões): basta alguma concessão, de admin ou de criação.
/// </param>
public sealed record TenantAdminAllowedMetadata(bool ProtectsAdminTargets, bool CreatesTenant = false, bool AnyGrant = false);

/// <summary>
/// Autoridade de admin de tenant (ADR-0044/0047): negada por padrão, aberta rota a rota, conferida no
/// cadastro a cada requisição e auditada de forma síncrona e fail-closed antes de toda escrita.
/// </summary>
internal static class TenantAdminAuthority
{
	/// <summary>
	/// Produto cujo banco no catálogo decide o destino da trilha (ADR-0047): sem banco do LogStream, o tenant
	/// administrado não recebe entrada de auditoria.
	/// </summary>
	private const string AuditProduct = "logstream";

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

	/// <summary>Indica se quem chama age com a autoridade de admin de tenant, e não como operador.</summary>
	/// <param name="caller">Principal da requisição.</param>
	public static bool IsTenantAdminCaller(ClaimsPrincipal caller) =>
		!ScopeAuthorization.HasScope(caller, SecureGateScopes.Admin);

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

	/// <summary>Abre a rota de criação de tenant à pessoa com concessão de criação (ADR-0047).</summary>
	/// <param name="builder">Rota.</param>
	public static RouteHandlerBuilder AllowTenantCreator(this RouteHandlerBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder
			.WithMetadata(new TenantAdminAllowedMetadata(ProtectsAdminTargets: false, CreatesTenant: true))
			.AddEndpointFilter(async (context, next) =>
				await AuthorizeCreationAsync(context.HttpContext).ConfigureAwait(false) ?? await next(context));
	}

	/// <summary>
	/// Abre uma LEITURA sem tenant na rota a quem tem alguma autoridade de admin de tenant (ADR-0048). Só
	/// <c>GET</c>: escrita sem tenant na rota não tem onde ser conferida nem auditada.
	/// </summary>
	/// <param name="builder">Rota.</param>
	public static RouteHandlerBuilder AllowAnyTenantAdmin(this RouteHandlerBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder
			.WithMetadata(new TenantAdminAllowedMetadata(ProtectsAdminTargets: false, AnyGrant: true))
			.AddEndpointFilter(async (context, next) =>
				await AuthorizeAnyGrantAsync(context.HttpContext).ConfigureAwait(false) ?? await next(context));
	}

	/// <summary>Recusa padrão: não diz se o tenant existe nem por que a autoridade não vale.</summary>
	public static IResult Forbidden() =>
		Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Acesso negado.");

	// null = segue para o handler; qualquer outro valor é a resposta
	private static async Task<IResult?> AuthorizeAsync(HttpContext httpContext, bool protectsAdminTargets)
	{
		var principal = httpContext.User;

		// O operador segue exatamente como antes desta ADR: sem conferência de concessão, sem esta trilha
		if (!IsTenantAdminCaller(principal))
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
			|| await ResolveOwnerTenantAsync(services, userId, cancellationToken).ConfigureAwait(false) is not { } ownerTenantId)
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

		var destination = await HasAuditDatabaseAsync(services, tenantId, cancellationToken).ConfigureAwait(false)
			? tenantId
			: ownerTenantId;

		return await AuditAsync(httpContext, auditor, userId, destination, tenantId, targetUserId).ConfigureAwait(false);
	}

	private static async Task<IResult?> AuthorizeAnyGrantAsync(HttpContext httpContext)
	{
		var principal = httpContext.User;

		if (!IsTenantAdminCaller(principal))
		{
			return null;
		}

		var services = httpContext.RequestServices;
		var cancellationToken = httpContext.RequestAborted;

		return !HttpMethods.IsGet(httpContext.Request.Method)
			|| !services.GetRequiredService<ITenantAdminAuditor>().IsConfigured
			|| !Guid.TryParse(principal.FindFirstValue(SeccoClaims.Subject), out var userId)
			|| !await services.GetRequiredService<ITenantAdminGrantRepository>()
				.HasAnyAuthorityAsync(userId, cancellationToken).ConfigureAwait(false)
			|| await ResolveOwnerTenantAsync(services, userId, cancellationToken).ConfigureAwait(false) is null
				? Forbidden()
				: null;
	}

	private static async Task<IResult?> AuthorizeCreationAsync(HttpContext httpContext)
	{
		var principal = httpContext.User;

		if (!IsTenantAdminCaller(principal))
		{
			return null;
		}

		var services = httpContext.RequestServices;
		var auditor = services.GetRequiredService<ITenantAdminAuditor>();
		var cancellationToken = httpContext.RequestAborted;

		if (!auditor.IsConfigured
			|| !Guid.TryParse(principal.FindFirstValue(SeccoClaims.Subject), out var userId)
			|| !await services.GetRequiredService<ITenantAdminGrantRepository>()
				.HasCreatorGrantAsync(userId, cancellationToken).ConfigureAwait(false)
			|| await ResolveOwnerTenantAsync(services, userId, cancellationToken).ConfigureAwait(false) is not { } ownerTenantId)
		{
			return Forbidden();
		}

		// O tenant ainda não existe: a criação vai para a trilha do tenant dono de quem cria (ADR-0047)
		return await AuditAsync(httpContext, auditor, userId, ownerTenantId, administeredTenantId: null, targetUserId: null)
			.ConfigureAwait(false);
	}

	private static async Task<IResult?> AuditAsync(
		HttpContext httpContext,
		ITenantAdminAuditor auditor,
		Guid userId,
		Guid destinationTenantId,
		Guid? administeredTenantId,
		Guid? targetUserId)
	{
		var principal = httpContext.User;
		var operation = httpContext.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "desconhecida";

		var recorded = await auditor.RecordAsync(
			new TenantAdminAuditRecord(
				userId,
				principal.FindFirstValue("name"),
				destinationTenantId,
				principal.FindFirstValue("client_id") ?? principal.FindFirstValue("azp"),
				operation,
				targetUserId,
				administeredTenantId),
			httpContext.RequestAborted).ConfigureAwait(false);

		return recorded
			? null
			: Results.Problem(
				statusCode: StatusCodes.Status503ServiceUnavailable,
				title: "Trilha de auditoria indisponível: a operação não foi feita.");
	}

	// Destino da trilha (ADR-0047): o tenant administrado só recebe a entrada se tiver banco do LogStream no
	// catálogo — senão a primeira escrita de um tenant novo, que é provisionar o banco, nunca passaria
	private static async Task<bool> HasAuditDatabaseAsync(IServiceProvider services, Guid tenantId, CancellationToken cancellationToken)
	{
		using (services.GetRequiredService<ITenantAccessScope>().EnterTenant(tenantId))
		{
			return await services.GetRequiredService<ITenantRepository>()
				.GetDatabaseAsync(tenantId, AuditProduct, cancellationToken).ConfigureAwait(false) is not null;
		}
	}

	// Estado ATUAL da conta (ADR-0032): o token pode ter sido emitido antes de a conta ser desativada. Devolve o
	// tenant dono da conta, ou nulo se ela não pode mais agir
	private static async Task<Guid?> ResolveOwnerTenantAsync(IServiceProvider services, Guid userId, CancellationToken cancellationToken)
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
					cancellationToken).ConfigureAwait(false)
				? user.TenantId
				: null;
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
