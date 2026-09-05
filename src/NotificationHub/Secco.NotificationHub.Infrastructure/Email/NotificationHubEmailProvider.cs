namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>
/// Provider de envio de e-mail (issue #14). Mesma forma da seleção de provider de banco
/// (ADR-0018): enum do próprio produto, default explícito, seleção na composição.
/// </summary>
/// <remarks>
/// O que troca entre um e outro é <b>apenas</b> a implementação de <see cref="IEmailSender"/> —
/// a porta já estava no lugar certo desde a Fase 8, então esta issue não redesenha nada, só
/// acrescenta um implementador e a forma de escolher.
/// </remarks>
public enum NotificationHubEmailProvider
{
	/// <summary>SMTP via MailKit. Default — atende instalação on-premise com servidor próprio.</summary>
	Smtp = 0,

	/// <summary>API HTTP do SendGrid. Atende instalação em nuvem sem SMTP disponível.</summary>
	SendGrid = 1,
}
