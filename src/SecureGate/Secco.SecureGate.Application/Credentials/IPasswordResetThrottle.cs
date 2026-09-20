namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Limite de pedidos de recuperação, por conta e por IP (ADR-0033).
/// </summary>
/// <remarks>
/// Existe contra duas coisas: inundar a caixa de entrada de alguém com links de redefinição, e
/// usar o formulário público como gerador de e-mails em massa. Não é proteção contra enumeração
/// de contas — essa é a resposta idêntica da página, que vale inclusive quando o limite estoura.
/// <para>
/// O limite vale <b>por instância</b> (ADR-0035): com N instâncias, o teto efetivo é N vezes o
/// configurado. É mitigação, não garantia.
/// </para>
/// </remarks>
public interface IPasswordResetThrottle
{
	/// <summary>
	/// Consome uma permissão para o par (conta, origem). <c>false</c> significa "não envie" — e
	/// nunca "responda diferente".
	/// </summary>
	/// <param name="email">E-mail normalizado informado no formulário.</param>
	/// <param name="remoteAddress">Endereço de origem da requisição, quando conhecido.</param>
	bool TryAcquire(string email, string? remoteAddress);
}
