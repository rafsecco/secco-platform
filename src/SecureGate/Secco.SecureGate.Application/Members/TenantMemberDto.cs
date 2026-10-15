namespace Secco.SecureGate.Application.Members;

/// <summary>Vínculo de uma pessoa com o tenant (ADR-0040) — sem segredos.</summary>
/// <param name="UserId">Identificador da pessoa (o <c>sub</c> dos tokens).</param>
/// <param name="Email">E-mail.</param>
/// <param name="DisplayName">Nome de exibição, opcional.</param>
/// <param name="OwnerTenantId">Tenant dono da conta (<c>User.TenantId</c>).</param>
/// <param name="IsActive">Se o vínculo vale para emitir token neste tenant.</param>
/// <param name="CreatedAt">Momento em que o vínculo nasceu.</param>
public sealed record TenantMemberDto(
	Guid UserId,
	string Email,
	string? DisplayName,
	Guid OwnerTenantId,
	bool IsActive,
	DateTimeOffset CreatedAt);
