namespace Secco.SecureGate.Application.Credentials;

/// <summary>Eventos de credencial registrados na trilha (ADR-0033).</summary>
public enum CredentialAuditEvent
{
	/// <summary>Senha definida pelo dono — convite aceito ou troca pela tela da conta.</summary>
	PasswordSet,

	/// <summary>Senha redefinida por link de recuperação ou por ação do admin.</summary>
	PasswordReset,

	/// <summary>Convite enviado ou reenviado.</summary>
	InviteSent,

	/// <summary>Pedido de recuperação de uma conta que existe e aceita login local.</summary>
	RecoveryRequested,

	/// <summary>Link recusado: token inválido, expirado ou já usado.</summary>
	LinkRejected,

	/// <summary>Login local desligado — a senha foi apagada.</summary>
	LocalLoginDisabled,

	/// <summary>Login local religado — segue um convite.</summary>
	LocalLoginEnabled,
}

/// <summary>
/// Trilha dos eventos de credencial, gravada no tenant do próprio usuário.
/// </summary>
/// <remarks>
/// <b>Best-effort por decisão (ADR-0033):</b> falha de auditoria não impede a operação. Recuperar
/// uma conta e revogar sessões são justamente o que alguém precisa fazer durante um incidente —
/// prendê-las à disponibilidade do LogStream transformaria uma indisponibilidade de log em
/// indisponibilidade de recuperação de conta. A elevação (ADR-0031) segue no extremo oposto,
/// fail-closed, por ser privilégio atravessando a fronteira de tenant.
/// </remarks>
public interface ICredentialAuditor
{
	/// <summary>Registra o evento. Nunca lança: o chamador não tem decisão a tomar sobre a falha.</summary>
	/// <param name="auditEvent">Evento ocorrido.</param>
	/// <param name="userId">Usuário dono da credencial.</param>
	/// <param name="tenantId">Tenant do usuário, lido do cadastro — nunca de claim.</param>
	/// <param name="userName">Snapshot legível do usuário, para a trilha.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RecordAsync(
		CredentialAuditEvent auditEvent,
		Guid userId,
		Guid tenantId,
		string? userName,
		CancellationToken cancellationToken = default);
}
