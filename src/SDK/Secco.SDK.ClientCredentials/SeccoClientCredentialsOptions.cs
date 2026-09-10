namespace Secco.SDK.ClientCredentials;

/// <summary>
/// Credenciais OAuth2 client credentials para um client de produto (<c>Secco.&lt;Produto&gt;.Client</c>)
/// autenticar chamadas de máquina a máquina contra o emissor da plataforma (o SecureGate).
/// Ausência de TODAS as três chaves mantém o client sem autenticação — modo válido, usado em
/// DEV com um token HS256 emitido fora da plataforma; presença PARCIAL é erro de configuração
/// e nunca degrada em silêncio (ADR-0020) — ver <see cref="Validate"/>.
/// </summary>
public sealed class SeccoClientCredentialsOptions
{
	/// <summary>URL base do emissor (o SecureGate) — o handler completa com <c>connect/token</c>.</summary>
	public string? AuthorityUrl { get; set; }

	/// <summary>Identificador do client OAuth (client credentials).</summary>
	public string? ClientId { get; set; }

	/// <summary>Segredo do client OAuth. Nunca aparece em log, mensagem de exceção ou <c>ToString</c> (ADR-0020).</summary>
	public string? ClientSecret { get; set; }

	/// <summary>
	/// Seção de configuração de onde estas opções foram lidas — usada apenas para compor a
	/// mensagem de erro de <see cref="Validate"/>. Atribuída durante o bind (ver
	/// <see cref="SeccoClientCredentialsExtensions.AddSeccoClientCredentialsOptions"/>); uma
	/// instância construída fora dali (por exemplo em testes) mantém o valor padrão.
	/// </summary>
	public string SectionKey { get; set; } = "(seção não identificada)";

	/// <summary>Indica se alguma das três chaves foi configurada (liga o modo autenticado).</summary>
	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(AuthorityUrl)
		|| !string.IsNullOrWhiteSpace(ClientId)
		|| !string.IsNullOrWhiteSpace(ClientSecret);

	/// <summary>
	/// Valida que as três chaves obrigatórias estão presentes e que <see cref="AuthorityUrl"/>
	/// é uma URL http(s) absoluta. Chamar somente depois de confirmar <see cref="IsConfigured"/>:
	/// esta validação é sobre configuração PARCIAL — ausência total é um modo válido (sem
	/// autenticação), não um erro.
	/// </summary>
	/// <exception cref="InvalidOperationException">Alguma chave obrigatória está ausente ou é inválida.</exception>
	public void Validate()
	{
		var missing = new List<string>();

		if (string.IsNullOrWhiteSpace(AuthorityUrl))
		{
			missing.Add(nameof(AuthorityUrl));
		}

		if (string.IsNullOrWhiteSpace(ClientId))
		{
			missing.Add(nameof(ClientId));
		}

		if (string.IsNullOrWhiteSpace(ClientSecret))
		{
			missing.Add(nameof(ClientSecret));
		}

		if (missing.Count > 0)
		{
			throw new InvalidOperationException(
				$"Credenciais client credentials parcialmente configuradas — faltam: {string.Join(", ", missing)} " +
				$"(seção '{SectionKey}'). Configure todas as chaves ou remova-as para operar sem autenticação (DEV).");
		}

		if (!Uri.TryCreate(AuthorityUrl, UriKind.Absolute, out var uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				$"'{SectionKey}:{nameof(AuthorityUrl)}' deve ser uma URL http(s) absoluta.");
		}
	}
}
