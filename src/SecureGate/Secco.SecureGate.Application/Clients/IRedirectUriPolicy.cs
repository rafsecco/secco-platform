namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Se <c>http://</c> é tolerado em redirect URI (só em Development). Porta para a Application não
/// conhecer o ambiente de hospedagem (ADR-0002).
/// </summary>
public interface IRedirectUriPolicy
{
	/// <summary>Verdadeiro quando redirect URI <c>http://</c> é aceita.</summary>
	bool AllowInsecure { get; }
}
