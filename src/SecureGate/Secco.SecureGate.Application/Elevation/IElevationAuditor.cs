namespace Secco.SecureGate.Application.Elevation;

/// <summary>Registro de uma troca de token por elevação (ADR-0031, invariante 7).</summary>
/// <param name="UserId">Quem elevou — o <c>sub</c> do token emitido.</param>
/// <param name="TenantId">
/// Tenant do usuário, lido do CADASTRO e nunca de claim do token de entrada. É onde a entrada
/// de auditoria é gravada, para que os admins daquela empresa vejam quem, entre os seus, elevou.
/// </param>
/// <param name="UserName">Snapshot legível do usuário no momento da troca.</param>
/// <param name="ClientId">Client que pediu a troca.</param>
/// <param name="Scopes">Escopos concedidos no token emitido.</param>
/// <param name="ExpiresAt">Expiração do token emitido.</param>
public sealed record ElevationAuditRecord(
	Guid UserId,
	Guid TenantId,
	string? UserName,
	string? ClientId,
	IReadOnlyList<string> Scopes,
	DateTimeOffset ExpiresAt);

/// <summary>
/// Porta de auditoria das trocas de elevação (ADR-0031 e emenda de 2026-09-13). A troca só é
/// emitida DEPOIS de o registro ser aceito: troca não auditada não acontece.
/// </summary>
public interface IElevationAuditor
{
	/// <summary>
	/// Indica se há identidade de auditoria configurada. Sem ela a capacidade de elevação fica
	/// desligada — opt-in por configuração e fail-closed, como a automação de provisionamento
	/// (ADR-0028) e a federação (ADR-0026).
	/// </summary>
	bool IsConfigured { get; }

	/// <summary>
	/// Grava o registro. Devolve <c>false</c> em qualquer falha, sem lançar — quem chama recusa a
	/// troca. Nunca devolve <c>true</c> sem o registro ter sido aceito pelo destino.
	/// </summary>
	/// <param name="record">O que registrar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> RecordAsync(ElevationAuditRecord record, CancellationToken cancellationToken = default);
}
