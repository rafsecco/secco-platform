using System.Globalization;
using Secco.SDK.Email;
using Secco.SecureGate.Application.Credentials;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Monta e envia os e-mails de credencial (ADR-0033).
/// </summary>
/// <remarks>
/// O link vem da <see cref="CredentialOptions.PublicBaseUrl"/> e <b>nunca</b> do header
/// <c>Host</c> (ADR-0020). Corpo em texto puro, sem HTML: um e-mail de recuperação é o alvo
/// preferido de phishing, e quanto menos ele parecer um formulário, melhor.
/// </remarks>
/// <param name="sender">Porta de envio do SDK.</param>
/// <param name="options">Configuração de credencial.</param>
internal sealed class SeccoEmailCredentialMailer(ISeccoEmailSender sender, CredentialOptions options)
	: ICredentialMailer
{
	/// <summary>Caminho da página que define a primeira senha.</summary>
	public const string SetPasswordPath = "/conta/definir-senha";

	/// <summary>Caminho da página de redefinição.</summary>
	public const string ResetPasswordPath = "/conta/redefinir-senha";

	/// <summary>Caminho da página que confirma a troca de e-mail.</summary>
	public const string ConfirmEmailPath = "/conta/confirmar-email";

	/// <inheritdoc />
	public Task SendInviteAsync(string recipient, Guid userId, string token, CancellationToken cancellationToken = default)
	{
		var link = Link(SetPasswordPath, userId, token);
		var horas = options.InviteLifetimeHours.ToString(CultureInfo.InvariantCulture);

		return sender.SendAsync(
			recipient,
			"Defina a senha da sua conta",
			$"""
			Sua conta foi criada e falta só a senha, que ninguém além de você escolhe.

			Defina a senha aqui: {link}

			O link vale por {horas} horas. Se ele expirar, peça outro em "esqueci minha senha".
			Se você não esperava este e-mail, ignore-o: sem a senha, a conta não entra.
			""",
			cancellationToken);
	}

	/// <inheritdoc />
	public Task SendResetAsync(string recipient, Guid userId, string token, CancellationToken cancellationToken = default)
	{
		var link = Link(ResetPasswordPath, userId, token);
		var minutos = options.ResetLifetimeMinutes.ToString(CultureInfo.InvariantCulture);

		return sender.SendAsync(
			recipient,
			"Redefinição de senha",
			$"""
			Alguém pediu a redefinição da senha desta conta.

			Redefina a senha aqui: {link}

			O link vale por {minutos} minutos e só pode ser usado uma vez.
			Se não foi você, ignore este e-mail — a senha atual continua valendo.
			""",
			cancellationToken);
	}

	/// <inheritdoc />
	public Task SendEmailChangeConfirmationAsync(
		string newRecipient,
		Guid userId,
		string newEmail,
		string token,
		CancellationToken cancellationToken = default)
	{
		var link = options.BuildLink(
			ConfirmEmailPath,
			$"userId={userId}&email={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(token)}");
		var minutos = options.ResetLifetimeMinutes.ToString(CultureInfo.InvariantCulture);

		return sender.SendAsync(
			newRecipient,
			"Confirme seu novo e-mail",
			$"""
			Pediram para que este endereço passe a ser o acesso de uma conta da plataforma.

			Confirme aqui: {link}

			O link vale por {minutos} minutos e só serve para este endereço.
			Se não foi você, ignore este e-mail: sem a confirmação, nada muda.
			""",
			cancellationToken);
	}

	/// <inheritdoc />
	public Task SendEmailChangeNoticeAsync(
		string currentRecipient,
		string newEmail,
		CancellationToken cancellationToken = default) =>
		sender.SendAsync(
			currentRecipient,
			"Pediram a troca do e-mail da sua conta",
			$"""
			Pediram para trocar o e-mail desta conta para {Mask(newEmail)}.

			Se foi você, confirme pelo link enviado ao endereço novo. Se não foi, troque sua senha
			agora e fale com o administrador do seu tenant: quem pediu tem acesso à sua sessão.
			""",
			cancellationToken);

	/// <summary>Mascara o destino: quem não tem a caixa nova não descobre qual é.</summary>
	private static string Mask(string email)
	{
		var at = email.IndexOf('@', StringComparison.Ordinal);

		return at <= 1
			? "***"
			: string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
	}

	/// <inheritdoc />
	public Task SendPasswordChangedNoticeAsync(string recipient, CancellationToken cancellationToken = default) =>
		sender.SendAsync(
			recipient,
			"Sua senha foi alterada",
			"""
			A senha desta conta acabou de ser alterada, e as sessões abertas foram encerradas.

			Se foi você, não há nada a fazer. Se não foi, fale com o administrador do seu tenant
			imediatamente: alguém com acesso ao seu e-mail ou à sua senha antiga fez a troca.
			""",
			cancellationToken);

	private string Link(string path, Guid userId, string token) =>
		options.BuildLink(path, $"userId={userId}&token={Uri.EscapeDataString(token)}");
}
