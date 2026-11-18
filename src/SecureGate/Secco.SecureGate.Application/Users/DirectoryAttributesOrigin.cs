namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Quem gravou os atributos de pessoa da conta (cargo, departamento, telefone, gestor): a sincronização com o
/// diretório federado (ADR-0055). <c>None</c> enquanto nunca sincronizados.
/// </summary>
public enum DirectoryAttributesOrigin
{
	/// <summary>Os atributos nunca foram gravados pelo diretório.</summary>
	None = 0,

	/// <summary>Gravados pela sincronização com o diretório federado.</summary>
	Directory = 1,
}
