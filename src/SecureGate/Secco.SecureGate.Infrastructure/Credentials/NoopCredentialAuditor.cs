using Secco.SecureGate.Application.Credentials;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Trilha de credencial sem destino configurado: não registra nada e não atrapalha ninguém.
/// </summary>
/// <remarks>
/// Sem a seção de auditoria, a <b>elevação</b> fica desligada (ADR-0031, fail-closed), mas o ciclo
/// de credencial continua funcionando — recuperar a própria conta não pode depender de a empresa
/// ter configurado uma identidade de auditoria (ADR-0033).
/// </remarks>
internal sealed class NoopCredentialAuditor : ICredentialAuditor
{
	/// <inheritdoc />
	public Task RecordAsync(
		CredentialAuditEvent auditEvent,
		Guid userId,
		Guid tenantId,
		string? userName,
		CancellationToken cancellationToken = default) => Task.CompletedTask;
}
