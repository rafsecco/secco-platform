namespace Secco.SecureGate.Application.Members;

/// <summary>Regras de vínculo compartilhadas pelos handlers (ADR-0040).</summary>
internal static class MemberRules
{
	/// <summary>
	/// O tenant de plataforma só aceita pessoas de que a plataforma é dona: operador é quem a plataforma
	/// governa, e um vínculo de plataforma para pessoa de outro dono não daria nada hoje e seria
	/// armadilha para o futuro.
	/// </summary>
	/// <param name="tenantId">Tenant do vínculo.</param>
	/// <param name="ownerTenantId">Tenant dono da conta.</param>
	public static bool IsForeignToPlatform(Guid tenantId, Guid ownerTenantId) =>
		tenantId == SecureGatePlatform.TenantId && ownerTenantId != SecureGatePlatform.TenantId;

	/// <summary>Se quem chama é a própria pessoa do vínculo (o <c>sub</c> vem do token, nunca do corpo).</summary>
	/// <param name="callerSubject">Claim <c>sub</c> de quem chama.</param>
	/// <param name="userId">Pessoa do vínculo.</param>
	public static bool IsSelf(string? callerSubject, Guid userId) =>
		string.Equals(callerSubject, userId.ToString(), StringComparison.OrdinalIgnoreCase);
}
