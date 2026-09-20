namespace Secco.SecureGate.Infrastructure.Elevation;

/// <summary>
/// Identidade de serviço que grava a auditoria das trocas de elevação (seção
/// <c>SecureGate:ElevationAudit</c>, ADR-0031 e emenda de 2026-09-13).
/// </summary>
/// <remarks>
/// A seção inteira ausente é válida e significa <b>elevação desligada</b>: sem identidade de
/// auditoria não há como auditar, e troca não auditada não acontece. O que não é válido é a seção
/// declarada pela metade — aí a intenção era ligar o recurso, e falhar no startup é melhor que
/// descobrir na primeira elevação (ADR-0020, mesma postura da automação de provisionamento).
/// <para>
/// O segredo vem daqui, de configuração, e nunca do seed de referência: aquele seed roda em todos
/// os ambientes, e segredo semeado seria segredo conhecido em produção.
/// </para>
/// </remarks>
public sealed class ElevationAuditOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	/// <remarks>
	/// Era <c>SecureGate:ElevationAudit</c> até a ADR-0033: a mesma identidade passou a registrar
	/// também os eventos de credencial, então o nome deixou de descrever o escopo. O antigo segue
	/// aceito (<see cref="LegacySectionKey"/>), com aviso no startup.
	/// </remarks>
	public const string SectionKey = "SecureGate:Audit";

	/// <summary>Nome anterior da seção, aceito para não quebrar instalação existente.</summary>
	public const string LegacySectionKey = "SecureGate:ElevationAudit";

	/// <summary>URL base da API do LogStream, onde a auditoria é gravada.</summary>
	public string? LogStreamBaseUrl { get; set; }

	/// <summary>URL base do emissor — o próprio SecureGate — para o client credentials da identidade de auditoria.</summary>
	public string? AuthorityUrl { get; set; }

	/// <summary>Client id da identidade de auditoria (o client OIDC com o papel <c>installation-auditor</c>).</summary>
	public string? ClientId { get; set; }

	/// <summary>Segredo da identidade de auditoria. Nunca logado nem citado em mensagem de erro.</summary>
	public string? ClientSecret { get; set; }

	/// <summary>
	/// Verdadeiro quando os valores vieram do nome antigo da seção. Só orienta o aviso de startup;
	/// não muda comportamento nenhum.
	/// </summary>
	public bool UsedLegacySection { get; set; }

	/// <summary>Indica se alguma chave da seção foi declarada — intenção de ligar a elevação.</summary>
	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(LogStreamBaseUrl)
		|| !string.IsNullOrWhiteSpace(AuthorityUrl)
		|| !string.IsNullOrWhiteSpace(ClientId)
		|| !string.IsNullOrWhiteSpace(ClientSecret);

	/// <summary>
	/// Valida o conjunto. Sem nenhuma chave é válido (recurso desligado); com alguma chave, todas são
	/// exigidas. A mensagem lista NOMES de chave, nunca valores.
	/// </summary>
	/// <param name="error">Motivo da invalidez, quando houver.</param>
	internal bool TryValidate(out string? error)
	{
		error = null;

		if (!IsConfigured)
		{
			return true;
		}

		var missing = new List<string>();

		if (string.IsNullOrWhiteSpace(LogStreamBaseUrl))
		{
			missing.Add(nameof(LogStreamBaseUrl));
		}

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
			error = $"Auditoria de elevação parcialmente configurada — faltam: {string.Join(", ", missing)} "
				+ $"(seção '{SectionKey}'). Configure todas as chaves, ou remova a seção para manter a elevação desligada.";

			return false;
		}

		if (!IsHttpUrl(LogStreamBaseUrl) || !IsHttpUrl(AuthorityUrl))
		{
			error = $"'{SectionKey}:LogStreamBaseUrl' e '{SectionKey}:AuthorityUrl' devem ser URLs http(s) absolutas.";

			return false;
		}

		return true;
	}

	private static bool IsHttpUrl(string? value) =>
		Uri.TryCreate(value, UriKind.Absolute, out var uri)
		&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
