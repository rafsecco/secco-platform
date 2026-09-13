namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de concessão/renovação de elevação (ADR-0031).</summary>
/// <param name="ExpiresAt">Expiração opcional da concessão; ausente/nula = sem expiração.</param>
public sealed record GrantElevationRequest(DateTimeOffset? ExpiresAt);
