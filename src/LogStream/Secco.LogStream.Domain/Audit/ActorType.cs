namespace Secco.LogStream.Domain.Audit;

/// <summary>
/// Natureza do ator de uma <see cref="AuditEntry"/>. O ator é declarado pelo produto
/// chamador, não derivado do token: quando a chamada é por client credentials, o <c>sub</c>
/// do token é a máquina, não a pessoa que agiu — não há alternativa (ver design doc,
/// seção de segurança).
/// </summary>
public enum ActorType
{
	/// <summary>Pessoa autenticada (usuário final).</summary>
	User = 0,

	/// <summary>Cliente de máquina (client credentials, job, integração).</summary>
	Client = 1,
}
