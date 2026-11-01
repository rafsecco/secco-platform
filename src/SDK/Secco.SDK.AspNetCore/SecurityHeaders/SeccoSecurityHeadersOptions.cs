namespace Secco.SDK.AspNetCore.SecurityHeaders;

/// <summary>Como a CSP é entregue ao navegador (ADR-0057).</summary>
public enum SeccoCspMode
{
	/// <summary>O navegador bloqueia o que viola a política.</summary>
	Enforce,

	/// <summary>
	/// O navegador só relata (<c>Content-Security-Policy-Report-Only</c>), para adoção gradual.
	/// <c>frame-ancestors</c> não vale nesse modo; o <c>X-Frame-Options</c> continua protegendo.
	/// </summary>
	ReportOnly,
}

/// <summary>
/// Cabeçalhos de segurança de resposta (ADR-0057). Os padrões já são seguros; cada ajuste é uma
/// opção nomeada e validada na subida, e nenhuma delas afrouxa <c>script-src</c> ou <c>style-src</c>.
/// </summary>
public sealed class SeccoSecurityHeadersOptions
{
	/// <summary>Enforce (padrão) ou ReportOnly.</summary>
	public SeccoCspMode Mode { get; set; } = SeccoCspMode.Enforce;

	/// <summary>Coletor externo de relatórios de violação (<c>report-uri</c>), HTTPS. O SDK não hospeda coletor.</summary>
	public Uri? ReportUri { get; set; }

	/// <summary>Origens extras de script, além de <c>'self'</c> e do nonce (ex.: <c>https://cdn.exemplo.com</c>).</summary>
	public IList<string> ScriptSources { get; } = [];

	/// <summary>Origens extras de folha de estilo, além de <c>'self'</c> e do nonce.</summary>
	public IList<string> StyleSources { get; } = [];

	/// <summary>Origens extras de imagem, além de <c>'self'</c> e <c>data:</c>; aceita também <c>blob:</c>.</summary>
	public IList<string> ImageSources { get; } = [];

	/// <summary>Origens extras de fonte, além de <c>'self'</c>; aceita também <c>data:</c>.</summary>
	public IList<string> FontSources { get; } = [];

	/// <summary>Origens extras de <c>fetch</c>/WebSocket, além de <c>'self'</c>.</summary>
	public IList<string> ConnectSources { get; } = [];

	/// <summary>Quem pode emoldurar a página: <c>'self'</c> (padrão), origens HTTPS, ou só <c>'none'</c>.</summary>
	public IList<string> FrameAncestors { get; } = ["'self'"];

	/// <summary>
	/// Destinos de envio de formulário (<c>form-action</c>). Vazio (padrão) omite a diretiva:
	/// o navegador a aplica também aos redirects que seguem o POST, e um valor fixo quebraria o OIDC.
	/// </summary>
	public IList<string> FormActions { get; } = [];

	/// <summary>Recursos do <c>Permissions-Policy</c> liberados para a própria origem (ex.: <c>camera</c>).</summary>
	public IList<string> AllowedPermissions { get; } = [];

	/// <summary>Liga o <c>Strict-Transport-Security</c> fora de Development (padrão: ligado).</summary>
	public bool HstsEnabled { get; set; } = true;

	/// <summary>Prazo do HSTS (padrão: 365 dias).</summary>
	public TimeSpan HstsMaxAge { get; set; } = TimeSpan.FromDays(365);

	/// <summary>Estende o HSTS aos subdomínios — alcança outros sistemas no mesmo domínio (padrão: desligado).</summary>
	public bool HstsIncludeSubDomains { get; set; }

	/// <summary>Pede inclusão na lista de preload dos navegadores (padrão: desligado).</summary>
	public bool HstsPreload { get; set; }

	internal List<string> StyleAttributeValues { get; } = [];

	/// <summary>Proíbe emoldurar a página: <c>frame-ancestors 'none'</c> e <c>X-Frame-Options: DENY</c>.</summary>
	public SeccoSecurityHeadersOptions DenyFraming()
	{
		FrameAncestors.Clear();
		FrameAncestors.Add("'none'");
		return this;
	}

	/// <summary>
	/// Libera um valor <b>exato</b> de atributo <c>style="..."</c> (já decodificado, como o navegador o lê),
	/// pelo hash dele (<c>'unsafe-hashes'</c>). Qualquer outro valor continua bloqueado.
	/// </summary>
	/// <param name="value">Valor do atributo, por exemplo <c>--sc-setor-hue:152</c>.</param>
	public SeccoSecurityHeadersOptions AllowStyleAttribute(string value)
	{
		ArgumentException.ThrowIfNullOrEmpty(value);
		StyleAttributeValues.Add(value);
		return this;
	}
}
