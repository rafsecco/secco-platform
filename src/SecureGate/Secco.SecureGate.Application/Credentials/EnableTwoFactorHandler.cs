using Secco.SecureGate.Application.Sessions;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Liga o segundo fator depois de confirmar que o autenticador da pessoa está correto (entrega D).
/// </summary>
/// <remarks>
/// A confirmação não é cerimônia: ligar o 2FA com um autenticador mal configurado tranca a conta
/// no login seguinte, e o caminho de volta passaria por um administrador.
/// <para>
/// Como todo evento de credencial, ligar encerra as outras sessões (ADR-0032) e avisa o dono. Os
/// códigos de recuperação voltam <b>nesta resposta e em nenhum outro lugar</b>: no banco há só o
/// hash, e a tela os mostra uma única vez.
/// </para>
/// </remarks>
/// <param name="setup">Porta de cadastro do segundo fator.</param>
/// <param name="tokens">Porta de credencial, para o e-mail e o tenant da conta.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class EnableTwoFactorHandler(
	ITwoFactorSetup setup,
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Confirma o código e liga o segundo fator.</summary>
	/// <param name="userId">Usuário autenticado.</param>
	/// <param name="code">Código exibido pelo aplicativo autenticador.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Os códigos de recuperação, ou vazio quando o código não confere.</returns>
	public async Task<IReadOnlyList<string>> HandleAsync(
		Guid userId,
		string code,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null || !await setup.ConfirmAsync(userId, code, cancellationToken).ConfigureAwait(false))
		{
			return [];
		}

		var codes = await setup.GenerateRecoveryCodesAsync(userId, cancellationToken).ConfigureAwait(false);

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);
		await mailer.SendTwoFactorChangedNoticeAsync(account.Email, enabled: true, cancellationToken)
			.ConfigureAwait(false);
		await auditor.RecordAsync(
			CredentialAuditEvent.TwoFactorEnabled, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return codes;
	}
}
