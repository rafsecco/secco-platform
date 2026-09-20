namespace Secco.SecureGate.Application.Credentials;

/// <summary>Desfecho de consumir um link de credencial ou de trocar a senha.</summary>
public enum CredentialTokenOutcome
{
	/// <summary>Senha definida.</summary>
	Done,

	/// <summary>Token inválido, expirado ou já usado — a resposta é a mesma nos três casos.</summary>
	InvalidToken,

	/// <summary>Senha recusada pela política do Identity.</summary>
	WeakPassword,

	/// <summary>Conta desativada, de tenant inativo ou sem login local — nunca detalhado ao chamador.</summary>
	NotAllowed,
}

/// <summary>Conta, do ponto de vista do ciclo de credencial.</summary>
/// <param name="UserId">Identificador.</param>
/// <param name="TenantId">Tenant, lido do cadastro.</param>
/// <param name="Email">E-mail (também o username).</param>
/// <param name="HasPassword">Se já existe hash de senha.</param>
/// <param name="LocalLoginEnabled">Se a conta aceita senha local (ADR-0026: desligada = só diretório).</param>
/// <param name="CanReceiveCredentialMail">
/// Se a conta está em condição de receber link: ativa, não bloqueada, tenant ativo e login local ligado.
/// </param>
public sealed record CredentialAccount(
	Guid UserId,
	Guid TenantId,
	string Email,
	bool HasPassword,
	bool LocalLoginEnabled,
	bool CanReceiveCredentialMail);

/// <summary>
/// Geração e consumo dos links de credencial, sobre os provedores de token do ASP.NET Identity
/// (ADR-0033).
/// </summary>
/// <remarks>
/// O token embute o <c>SecurityStamp</c> da conta: definir ou trocar a senha muda o stamp e mata
/// todos os links pendentes juntos. É o que dá uso único sem tabela de tokens, sem expurgo e sem
/// mais um lugar onde um segredo fica em repouso.
/// </remarks>
public interface ICredentialTokens
{
	/// <summary>Conta pelo id; <c>null</c> se não existe.</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<CredentialAccount?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Conta pelo e-mail; <c>null</c> se não existe.</summary>
	/// <param name="email">E-mail informado pelo usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<CredentialAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

	/// <summary>Gera o token do convite (validade longa).</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<string> CreateInviteTokenAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Gera o token de redefinição (validade curta).</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<string> CreateResetTokenAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Consome um link e define a senha.</summary>
	/// <param name="userId">Usuário do link.</param>
	/// <param name="token">Token do link.</param>
	/// <param name="invite"><c>true</c> para convite; <c>false</c> para redefinição.</param>
	/// <param name="newPassword">Senha escolhida pela pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<CredentialTokenOutcome> SetPasswordAsync(
		Guid userId,
		string token,
		bool invite,
		string newPassword,
		CancellationToken cancellationToken = default);

	/// <summary>Troca a senha exigindo a atual.</summary>
	/// <param name="userId">Usuário autenticado.</param>
	/// <param name="currentPassword">Senha atual.</param>
	/// <param name="newPassword">Nova senha.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<CredentialTokenOutcome> ChangeOwnPasswordAsync(
		Guid userId,
		string currentPassword,
		string newPassword,
		CancellationToken cancellationToken = default);

	/// <summary>Apaga a senha da conta (desligar o login local).</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemovePasswordAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Liga ou desliga o login local da conta.</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="enabled">Novo estado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SetLocalLoginAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default);
}
