namespace Secco.SecureGate.Application.Credentials;

/// <summary>Estado do segundo fator de uma conta.</summary>
/// <param name="Enabled">2FA ligado.</param>
/// <param name="HasAuthenticator">Já existe chave cadastrada.</param>
/// <param name="RecoveryCodesLeft">Códigos de recuperação ainda válidos.</param>
public sealed record TwoFactorState(bool Enabled, bool HasAuthenticator, int RecoveryCodesLeft);

/// <summary>Chave nova, pronta para exibição.</summary>
/// <param name="FormattedKey">Chave em blocos de 4, para digitação manual.</param>
/// <param name="QrCodeDataUri">QR em <c>data:image/png;base64,...</c>, gerado no servidor.</param>
public sealed record TwoFactorEnrollment(string FormattedKey, string QrCodeDataUri);

/// <summary>
/// Cadastro e estado do segundo fator TOTP (entrega D), sobre o <c>AuthenticatorTokenProvider</c>
/// que o ASP.NET Identity já registra. Zero migration: a chave e os códigos de recuperação vivem
/// em <c>tb_user_tokens</c>, e <c>fl_two_factor_enabled</c> já existe em <c>tb_users</c>.
/// </summary>
/// <remarks>
/// O QR nunca é gerado por um serviço externo: o adaptador embute a imagem como <c>data:</c> URI
/// porque um gerador de terceiros receberia o segredo TOTP do usuário, o que anula o segundo fator
/// antes mesmo de ele existir (ADR-0020).
/// </remarks>
public interface ITwoFactorSetup
{
	/// <summary>Estado atual; <c>null</c> se a conta não existe.</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TwoFactorState?> GetStateAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gera uma chave nova e o QR correspondente; <c>null</c> se a conta não existe.
	/// </summary>
	/// <remarks>
	/// Recomeçar o cadastro invalida o QR anterior: a chave é sempre reescrita, nunca reaproveitada.
	/// </remarks>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TwoFactorEnrollment?> StartEnrollmentAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Confere o código digitado e, se válido, liga o segundo fator. Sem essa confirmação, um
	/// autenticador mal configurado trancaria a conta no login seguinte.
	/// </summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="code">Código de seis dígitos do aplicativo autenticador.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> ConfirmAsync(Guid userId, string code, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gera 10 códigos de recuperação novos, substituindo os anteriores. O Identity guarda só o
	/// hash: estes valores existem apenas nesta resposta.
	/// </summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Desliga o segundo fator e apaga a chave do autenticador — zera o cadastro em vez de isentar
	/// (religar exige cadastrar de novo).
	/// </summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task DisableAsync(Guid userId, CancellationToken cancellationToken = default);
}
