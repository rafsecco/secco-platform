namespace Secco.SDK.Email;

/// <summary>
/// Provider de envio de e-mail. Mesma forma da seleção de provider de banco (ADR-0018): enum
/// do próprio pacote, default explícito, seleção na composição.
/// </summary>
public enum SeccoEmailProvider
{
	/// <summary>SMTP via MailKit. Default — atende instalação on-premise com servidor próprio.</summary>
	Smtp = 0,

	/// <summary>API HTTP do SendGrid. Atende instalação em nuvem sem SMTP disponível.</summary>
	SendGrid = 1,
}
