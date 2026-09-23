using Secco.SecureGate.Application.Sessions;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Desliga o segundo fator (entrega D).
/// </summary>
/// <remarks>
/// Desligar <b>zera o cadastro</b>: a chave do autenticador é apagada junto. Religar exige
/// cadastrar de novo, e nunca reaproveita um QR antigo que possa estar guardado em algum lugar.
/// <para>
/// Quem decide se esta conta <i>pode</i> desligar é a camada de cima — o operador de instalação
/// não pode (ADR-0030), e essa regra vive na Api, junto do papel. Aqui o caso de uso é o efeito.
/// </para>
/// </remarks>
/// <param name="setup">Porta de cadastro do segundo fator.</param>
/// <param name="tokens">Porta de credencial, para o e-mail e o tenant da conta.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class DisableTwoFactorHandler(
	ITwoFactorSetup setup,
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Desliga o segundo fator da conta.</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		await setup.DisableAsync(userId, cancellationToken).ConfigureAwait(false);

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);
		await mailer.SendTwoFactorChangedNoticeAsync(account.Email, enabled: false, cancellationToken)
			.ConfigureAwait(false);
		await auditor.RecordAsync(
			CredentialAuditEvent.TwoFactorDisabled, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
