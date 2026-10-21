using System.Security.Claims;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.InstallationOperators;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Authorization;

/// <summary>
/// Token de operador designado (ADR-0049) em qualquer rota: a cada requisição, a designação e a conta são
/// reconferidas — tirar a pessoa da configuração vale na requisição seguinte, sem esperar o token expirar — e toda
/// escrita vai à trilha ANTES de acontecer, síncrona e fail-closed. Middleware, e não filtro de rota, porque o token
/// carrega <c>securegate:admin</c> e abre todas as rotas de operador: nenhuma pode escapar da conferência.
/// </summary>
internal sealed class DesignatedOperatorGuard(RequestDelegate next)
{
	/// <summary>Processa a requisição.</summary>
	/// <param name="context">Requisição.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var principal = context.User;

		if (principal.FindFirstValue(SecureGatePlatform.TokenExchangeClaim) != SecureGatePlatform.DesignatedOperatorCapability)
		{
			await next(context).ConfigureAwait(false);

			return;
		}

		var services = context.RequestServices;
		var cancellationToken = context.RequestAborted;
		var auditor = services.GetRequiredService<ITenantAdminAuditor>();

		if (!auditor.IsConfigured
			|| !Guid.TryParse(principal.FindFirstValue(SeccoClaims.Subject), out var userId)
			|| !services.GetRequiredService<IInstallationOperatorDesignations>().IsDesignated(userId)
			|| await TenantAdminAuthority.ResolveOwnerTenantAsync(services, userId, cancellationToken).ConfigureAwait(false)
				is not { } ownerTenantId)
		{
			await TenantAdminAuthority.Forbidden().ExecuteAsync(context).ConfigureAwait(false);

			return;
		}

		if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
		{
			Guid? routeTenant = TenantAdminAuthority.TryGetRouteGuid(context, out var tenantId, "tenantId", "id") ? tenantId : null;
			var destination = routeTenant is { } administered
				&& await TenantAdminAuthority.HasAuditDatabaseAsync(services, administered, cancellationToken).ConfigureAwait(false)
					? administered
					: ownerTenantId;

			var recorded = await auditor.RecordAsync(
				new TenantAdminAuditRecord(
					userId,
					principal.FindFirstValue("name"),
					destination,
					principal.FindFirstValue("client_id") ?? principal.FindFirstValue("azp"),
					context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "desconhecida",
					TenantAdminAuthority.TryGetRouteGuid(context, out var target, "userId") ? target : null,
					routeTenant,
					SecureGateScopes.Admin),
				cancellationToken).ConfigureAwait(false);

			if (!recorded)
			{
				await Results.Problem(
						statusCode: StatusCodes.Status503ServiceUnavailable,
						title: "Trilha de auditoria indisponível: a operação não foi feita.")
					.ExecuteAsync(context).ConfigureAwait(false);

				return;
			}
		}

		await next(context).ConfigureAwait(false);
	}
}
