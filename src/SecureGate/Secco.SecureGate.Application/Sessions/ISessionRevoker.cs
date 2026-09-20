namespace Secco.SecureGate.Application.Sessions;

/// <summary>Motivo registrado da revogação.</summary>
public enum SessionRevocationReason
{
	/// <summary>Admin pediu para encerrar as sessões.</summary>
	AdminRequest,

	/// <summary>Conta desativada.</summary>
	UserDeactivated,

	/// <summary>Usuário retirado de um perfil.</summary>
	RoleRemoved,

	/// <summary>Senha definida, trocada ou redefinida (ADR-0033).</summary>
	PasswordChanged,
}

/// <summary>Encerra todas as sessões de um usuário (ADR-0032).</summary>
public interface ISessionRevoker
{
	/// <summary>
	/// Troca a versão de sessão e revoga autorizações e tokens. Usuário inexistente não é erro: não há
	/// sessão a encerrar.
	/// </summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="reason">Motivo, para o log.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken = default);
}
