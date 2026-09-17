namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>Versão de sessão atual de um usuário (ADR-0032).</summary>
/// <param name="SessionVersion">Versão atual; nula quando revogada.</param>
/// <param name="Revoked">Sessão não pode ser aceita.</param>
public sealed record SessionVersionStatus(string? SessionVersion, bool Revoked);

/// <summary>
/// Fonte da versão de sessão atual — em produção, o SecureGate via <c>Secco.SecureGate.Client</c>.
/// </summary>
public interface ISessionVersionResolver
{
	/// <summary>
	/// Indica se a verificação está ativa. Falso só quando a fonte não está configurada (DEV standalone);
	/// uma fonte configurada que falha continua habilitada e recusa (fail-closed).
	/// </summary>
	bool IsEnabled { get; }

	/// <summary>Consulta a versão atual.</summary>
	/// <param name="subject">O <c>sub</c> do token.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	ValueTask<SessionVersionStatus> ResolveAsync(string subject, CancellationToken cancellationToken = default);
}
