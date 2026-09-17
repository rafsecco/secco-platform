namespace Secco.SecureGate.Application.Sessions;

/// <summary>Versão de sessão atual de um usuário, consultada pelos produtos (ADR-0032).</summary>
/// <param name="SessionVersion">Versão atual; nula quando revogada.</param>
/// <param name="Revoked">Sessão não pode ser aceita: conta inexistente, desativada, bloqueada ou de tenant inativo.</param>
public sealed record SessionVersionDto(string? SessionVersion, bool Revoked)
{
	/// <summary>Resposta única para todo caso recusado — não distingue inexistente de desativado.</summary>
	public static readonly SessionVersionDto RevokedState = new(null, true);
}
