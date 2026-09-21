using Secco.SecureGate.Application.Credentials;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Remove o vínculo de login externo de uma conta (entrega C).
/// </summary>
/// <remarks>
/// <b>Não bloqueia ninguém.</b> Enquanto a pessoa seguir no diretório e a federação do tenant
/// estiver ligada, o próximo login casa por e-mail e vincula de novo (ADR-0026). A operação serve
/// para vínculo morto — conta recriada no diretório com outro <c>oid</c>, pessoa que saiu de lá.
/// Quem quer barrar acesso desativa a conta ou desliga a federação do tenant.
/// <para>
/// A guarda é uma só: conta com o login local <b>desligado</b> não pode perder o vínculo, porque
/// ele é o único caminho de entrada. "Sem senha ainda" não impede — o convite e o "esqueci minha
/// senha" levam a pessoa até a senha (ADR-0033).
/// </para>
/// </remarks>
/// <param name="directory">Porta de provisionamento de usuários.</param>
/// <param name="tokens">Porta de credencial, para o estado da conta.</param>
/// <param name="auditor">Trilha (best-effort).</param>
public sealed class RemoveExternalLoginHandler(
	IUserDirectory directory,
	ICredentialTokens tokens,
	ICredentialAuditor auditor)
{
	/// <summary>Remove o vínculo do provedor informado.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="provider">Provedor externo (ex.: <c>EntraId</c>).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		Guid tenantId,
		Guid userId,
		string provider,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		// Usuário de outro tenant responde como inexistente (ADR-0020).
		if (account is null || account.TenantId != tenantId)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		if (!account.LocalLoginEnabled)
		{
			return Result.Failure(SecureGateErrors.Credentials.LastSignInPath);
		}

		if (!await directory.RemoveExternalLoginAsync(tenantId, userId, provider, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		await auditor.RecordAsync(
			CredentialAuditEvent.ExternalLoginRemoved, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
