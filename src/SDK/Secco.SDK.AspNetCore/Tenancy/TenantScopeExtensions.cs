using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Ambient;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Estabelece o tenant em escopos fora do pipeline HTTP — workers de background,
/// provisionamento, jobs (ADR-0015: o <c>tenant_id</c> viaja no payload e o SDK
/// restaura o contexto de tenant na execução).
/// </summary>
public static class TenantScopeExtensions
{
	/// <summary>
	/// Define o tenant do escopo de DI atual. Usar apenas em escopos criados manualmente
	/// (ex.: <c>CreateScope()</c> dentro de um <c>BackgroundService</c>) — no pipeline HTTP
	/// quem popula o contexto é o <see cref="SeccoTenancyMiddleware"/>.
	/// </summary>
	/// <param name="scopedServices">Provider do escopo criado manualmente.</param>
	/// <param name="tenantId">Tenant do item de trabalho em execução.</param>
	public static void SetTenant(this IServiceProvider scopedServices, Guid tenantId)
	{
		ArgumentNullException.ThrowIfNull(scopedServices);

		scopedServices.GetRequiredService<TenantContext>().TenantId = tenantId;

		// Fora do pipeline HTTP não há middleware para espelhar o tenant no contexto ambiente —
		// sem isto, um log emitido dentro de um job não saberia para qual tenant ir.
		SeccoAmbientContext.SetTenantId(tenantId);
	}
}
