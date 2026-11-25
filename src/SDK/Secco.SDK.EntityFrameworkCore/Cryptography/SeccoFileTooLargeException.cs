namespace Secco.SDK.EntityFrameworkCore.Cryptography;

/// <summary>
/// O conteúdo passou do tamanho máximo informado a <see cref="ISeccoFileCipher.EncryptAsync"/>
/// (ADR-0061).
/// </summary>
/// <remarks>
/// Separada de <see cref="SeccoSecretCipherException"/> porque não é falha criptográfica: é input
/// grande demais, e quem chama costuma traduzi-la em erro de negócio (<c>413</c> ou
/// <c>Result</c>, ADR-0004), enquanto a outra é falha de infraestrutura.
/// </remarks>
public sealed class SeccoFileTooLargeException : Exception
{
	/// <summary>Cria a exceção com o limite que foi ultrapassado.</summary>
	/// <param name="maxPlaintextLength">Limite em bytes.</param>
	public SeccoFileTooLargeException(long maxPlaintextLength)
		: base($"Conteúdo acima do tamanho máximo aceito ({maxPlaintextLength} bytes).")
	{
		MaxPlaintextLength = maxPlaintextLength;
	}

	/// <summary>Limite que foi ultrapassado, em bytes.</summary>
	public long MaxPlaintextLength { get; }
}
