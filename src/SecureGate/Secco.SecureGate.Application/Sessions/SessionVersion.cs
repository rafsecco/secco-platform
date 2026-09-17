using System.Security.Cryptography;
using System.Text;

namespace Secco.SecureGate.Application.Sessions;

/// <summary>
/// Versão de sessão (ADR-0032): resumo do <c>SecurityStamp</c> do Identity. Nunca o stamp cru — ele
/// participa da geração dos tokens de recuperação de senha.
/// </summary>
public static class SessionVersion
{
	/// <summary>Tamanho da versão, em caracteres Base64Url (96 bits).</summary>
	public const int Length = 16;

	/// <summary>Deriva a versão a partir do stamp atual.</summary>
	/// <param name="securityStamp">SecurityStamp do usuário.</param>
	public static string From(string? securityStamp)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty));

		return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..Length];
	}
}
