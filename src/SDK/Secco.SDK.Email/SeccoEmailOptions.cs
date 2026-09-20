namespace Secco.SDK.Email;

/// <summary>
/// Configuração de envio de e-mail. A seção concreta (ex.: <c>SecureGate:Email</c>,
/// <c>NotificationHub:Email</c>) é do produto que consome o pacote — ver
/// <see cref="SeccoEmailServiceCollectionExtensions.AddSeccoEmail(Microsoft.Extensions.DependencyInjection.IServiceCollection, string)"/>.
/// </summary>
/// <remarks>
/// As chaves de SMTP e de SendGrid convivem na mesma seção; <see cref="Provider"/> decide
/// quais são exigidas. Validação no startup (fail-fast, ADR-0020): configuração pela metade
/// não vira falha só no primeiro envio, quando já há notificação represada.
/// </remarks>
public sealed class SeccoEmailOptions
{
	/// <summary>Provider de envio. Default <see cref="SeccoEmailProvider.Smtp"/>.</summary>
	public SeccoEmailProvider Provider { get; set; } = SeccoEmailProvider.Smtp;

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
	/// lançar: configuração inválida é fluxo previsto, não excepcional (ADR-0004). A mensagem
	/// cita NOMES de chave, nunca valores.
	/// </summary>
	/// <param name="sectionKey">Chave da seção do produto (ex.: <c>SecureGate:Email</c>), só para a mensagem.</param>
	/// <param name="error">Descrição do primeiro problema; nula quando válido.</param>
	public bool TryValidate(string sectionKey, out string? error)
	{
		if (string.IsNullOrWhiteSpace(FromAddress))
		{
			error = $"'{sectionKey}:FromAddress' é obrigatório.";
			return false;
		}

		if (Provider == SeccoEmailProvider.Smtp && string.IsNullOrWhiteSpace(Host))
		{
			error = $"'{sectionKey}:Host' é obrigatório quando o provider é Smtp.";
			return false;
		}

		if (Provider == SeccoEmailProvider.SendGrid && string.IsNullOrWhiteSpace(ApiKey))
		{
			error = $"'{sectionKey}:ApiKey' é obrigatório quando o provider é SendGrid.";
			return false;
		}

		error = null;
		return true;
	}
}
