namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Relação de uma pessoa com o tenant em que é listada (ADR-0040). Valores estáveis no contrato:
/// o AdminPortal compara por texto.
/// </summary>
public static class MembershipStates
{
	/// <summary>O tenant é o dono da conta (<c>User.TenantId</c>); o vínculo nunca se suspende.</summary>
	public const string Owner = "Owner";

	/// <summary>Membro com vínculo ativo (recebe token para o tenant).</summary>
	public const string Member = "Member";

	/// <summary>Membro com vínculo suspenso (não recebe token; perfis e histórico preservados).</summary>
	public const string Suspended = "Suspended";
}
