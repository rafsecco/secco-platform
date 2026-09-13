using Secco.SecureGate.Application.Elevation;

namespace Secco.SecureGate.Infrastructure.Elevation;

/// <summary>
/// Auditor de uma instalação SEM identidade de auditoria configurada (ADR-0031, emenda de
/// 2026-09-13). Não audita — e, por não auditar, desliga a elevação: o handler da troca recusa
/// antes de consultar qualquer coisa.
/// </summary>
internal sealed class NotConfiguredElevationAuditor : IElevationAuditor
{
	/// <summary>Instância única — não há estado.</summary>
	public static readonly NotConfiguredElevationAuditor Instance = new();

	private NotConfiguredElevationAuditor()
	{
	}

	/// <inheritdoc />
	public bool IsConfigured => false;

	/// <inheritdoc />
	public Task<bool> RecordAsync(ElevationAuditRecord record, CancellationToken cancellationToken = default) =>
		Task.FromResult(false);
}
