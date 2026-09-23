using Secco.SecureGate.Application.Sessions;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Zera o cadastro do segundo fator de um usuário, a pedido de um administrador (entrega D).
/// </summary>
/// <remarks>
/// <b>Reset não isenta.</b> A chave e os códigos são apagados e a conta volta ao estado "sem 2FA
/// cadastrado" — sendo conta de operador, o próximo login cai direto no cadastro (ADR-0030). É a
/// diferença entre devolver o acesso a quem perdeu o dispositivo e abrir uma porta permanente.
/// <para>
/// Audita e <b>avisa o dono</b> justamente porque esta é a operação que um administrador
/// comprometido usaria para contornar o segundo fator de outra pessoa: ela precisa deixar rastro
/// e chegar à caixa da vítima (ADR-0020).
/// </para>
/// <para>
/// Idempotente (ADR-0034): conta sem 2FA percorre o mesmo caminho e responde igual.
/// </para>
/// </remarks>
/// <param name="setup">Porta de cadastro do segundo fator.</param>
/// <param name="tokens">Porta de credencial, para o tenant e o e-mail da conta.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class ResetTwoFactorHandler(
	ITwoFactorSetup setup,
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Zera o cadastro do usuário do tenant informado.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		// Usuário de outro tenant responde como inexistente (ADR-0020).
		if (account is null || account.TenantId != tenantId)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		await setup.DisableAsync(userId, cancellationToken).ConfigureAwait(false);

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);
		await mailer.SendTwoFactorChangedNoticeAsync(account.Email, enabled: false, cancellationToken)
			.ConfigureAwait(false);
		await auditor.RecordAsync(
			CredentialAuditEvent.TwoFactorReset, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
