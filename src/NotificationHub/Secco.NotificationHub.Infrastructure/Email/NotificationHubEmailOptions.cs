namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>
/// Configuração de envio de e-mail (seção <c>NotificationHub:Email</c>).
/// </summary>
/// <remarks>
/// As chaves de SMTP e de SendGrid convivem na mesma seção; <see cref="Provider"/> decide
/// quais são exigidas. Validação no startup (fail-fast, ADR-0020): configuração pela metade
/// não vira falha só no primeiro envio, quando já há notificação represada.
/// </remarks>
public sealed class NotificationHubEmailOptions
{
	/// <summary>Provider de envio (issue #14). Default <see cref="NotificationHubEmailProvider.Smtp"/>.</summary>
	public NotificationHubEmailProvider Provider { get; set; } = NotificationHubEmailProvider.Smtp;

	/// <summary>Chave de API do SendGrid. Exigida quando o provider é SendGrid. Nunca logada (ADR-0020).</summary>
	public string? ApiKey { get; set; }

	/// <summary>Host do servidor SMTP. Exigido quando o provider é SMTP.</summary>
	public string Host { get; set; } = string.Empty;

	/// <summary>Porta do servidor SMTP (default 587, STARTTLS).</summary>
	public int Port { get; set; } = 587;

	/// <summary>Usa STARTTLS (default true — nunca enviar credenciais em texto claro).</summary>
	public bool UseStartTls { get; set; } = true;

	/// <summary>Usuário de autenticação, quando o provider exigir.</summary>
	public string? Username { get; set; }

	/// <summary>Senha de autenticação — nunca logada (ADR-0020).</summary>
	public string? Password { get; set; }

	/// <summary>Endereço de remetente.</summary>
	public string FromAddress { get; set; } = string.Empty;

	/// <summary>Nome de exibição do remetente, quando houver.</summary>
	public string? FromName { get; set; }

	/// <summary>
	/// Valida o conjunto exigido pelo provider selecionado. Devolve o resultado em vez de
	/// lançar: configuração inválida é fluxo previsto, não excepcional (ADR-0004).
	/// </summary>
	/// <param name="error">Descrição do primeiro problema encontrado; nula quando válido.</param>
	public bool TryValidate(out string? error)
	{
		if (string.IsNullOrWhiteSpace(FromAddress))
		{
			error = "'NotificationHub:Email:FromAddress' é obrigatório.";
			return false;
		}

		if (Provider == NotificationHubEmailProvider.Smtp && string.IsNullOrWhiteSpace(Host))
		{
			error = "'NotificationHub:Email:Host' é obrigatório quando o provider é Smtp.";
			return false;
		}

		if (Provider == NotificationHubEmailProvider.SendGrid && string.IsNullOrWhiteSpace(ApiKey))
		{
			error = "'NotificationHub:Email:ApiKey' é obrigatório quando o provider é SendGrid.";
			return false;
		}

		error = null;
		return true;
	}
}
