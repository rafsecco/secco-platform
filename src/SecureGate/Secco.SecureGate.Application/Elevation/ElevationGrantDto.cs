namespace Secco.SecureGate.Application.Elevation;

/// <summary>Concessão de elevação, visão de gestão (ADR-0031).</summary>
/// <param name="UserId">Usuário que pode elevar.</param>
/// <param name="TenantId">Tenant do usuário.</param>
/// <param name="GrantedBy">Sub de quem concedeu (última concessão/renovação).</param>
/// <param name="CreatedAt">Momento da concessão original.</param>
/// <param name="ExpiresAt">Expiração, se houver; nulo = sem expiração.</param>
/// <param name="IsActive">Se a concessão está ativa neste instante (<see cref="Domain.Elevation.ElevationGrant.IsActiveAt"/>).</param>
public sealed record ElevationGrantDto(
	Guid UserId,
	Guid TenantId,
	string GrantedBy,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ExpiresAt,
	bool IsActive);
