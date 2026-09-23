using System.Globalization;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Configuração do ciclo de credencial (ADR-0033): validade dos links, limites do "esqueci minha
/// senha" e a base pública de onde os links são montados.
/// </summary>
/// <remarks>
/// A <see cref="PublicBaseUrl"/> vem de configuração e <b>nunca</b> do header <c>Host</c>
/// (ADR-0020): um <c>Host</c> forjado faria a plataforma enviar à vítima um link apontando para o
/// servidor do atacante — o e-mail sairia do remetente certo, com o texto certo, e entregaria a
/// conta. Fora de Development ela é obrigatória e o serviço não sobe sem ela.
/// <para>
/// As validades têm teto no código porque configuração também é input privilegiado: um convite de
/// "876000 horas" é um link de recuperação eterno na caixa de entrada de alguém.
/// </para>
/// </remarks>
public sealed class CredentialOptions
{
	/// <summary>Seção de onde estas opções são lidas.</summary>
	public const string SectionKey = "SecureGate:Credentials";

	/// <summary>Chave da base pública — mora um nível acima, por não ser só de credencial.</summary>
	public const string PublicBaseUrlKey = "SecureGate:PublicBaseUrl";

	/// <summary>Base assumida em Development, onde a API sobe na 4001 por convenção do repositório.</summary>
	public const string DevelopmentPublicBaseUrl = "https://localhost:4001";

	/// <summary>Teto da validade do convite: 30 dias.</summary>
	public const int MaxInviteLifetimeHours = 720;

	/// <summary>Teto da validade do link de redefinição: 2 horas.</summary>
	public const int MaxResetLifetimeMinutes = 120;

	/// <summary>Teto do piso de tempo da resposta do "esqueci minha senha".</summary>
	public const int MaxResponseFloorMilliseconds = 5_000;

	/// <summary>Base pública dos links (ex.: <c>https://id.empresa.com</c>). Obrigatória fora de Development.</summary>
	public string? PublicBaseUrl { get; set; }

	/// <summary>Validade do convite, em horas (padrão 72).</summary>
	public int InviteLifetimeHours { get; set; } = 72;

	/// <summary>Validade do link de redefinição, em minutos (padrão 30).</summary>
	public int ResetLifetimeMinutes { get; set; } = 30;

	/// <summary>Pedidos de recuperação aceitos por conta, por hora (padrão 3).</summary>
	public int ForgotPerAccountPerHour { get; set; } = 3;

	/// <summary>Pedidos de recuperação aceitos por IP, por hora (padrão 10).</summary>
	public int ForgotPerIpPerHour { get; set; } = 10;

	/// <summary>
	/// Nome que aparece no aplicativo autenticador da pessoa. Padrão "Secco SecureGate"; uma
	/// instalação com marca própria troca aqui.
	/// </summary>
	public string TwoFactorIssuer { get; set; } = "Secco SecureGate";

	/// <summary>
	/// Piso de tempo da resposta do "esqueci minha senha", em milissegundos (padrão 300). Sem ele,
	/// o tempo denuncia a existência da conta: só no caso "existe" há e-mail a enviar.
	/// </summary>
	public int ResponseFloorMilliseconds { get; set; } = 300;

	/// <summary>
	/// Base efetiva: a configurada ou, quando ausente, a local da convenção de portas — caso que
	/// só existe em Development, porque fora dele a validação do startup já barrou a ausência.
	/// </summary>
	public Uri ResolveBaseUri() =>
		new(string.IsNullOrWhiteSpace(PublicBaseUrl)
			? DevelopmentPublicBaseUrl
			: PublicBaseUrl,
			UriKind.Absolute);

	/// <summary>Monta um link absoluto a partir da base pública, sem duplicar barras.</summary>
	/// <param name="path">Caminho da página (ex.: <c>/conta/definir-senha</c>).</param>
	/// <param name="query">Query string já codificada, sem o <c>?</c>.</param>
	public string BuildLink(string path, string query)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var absolute = new Uri(ResolveBaseUri(), path);

		return string.IsNullOrEmpty(query)
			? absolute.ToString()
			: absolute + "?" + query;
	}

	/// <summary>
	/// Valida o conjunto. Devolve o resultado em vez de lançar: configuração inválida é fluxo
	/// previsto (ADR-0004). A mensagem cita nomes de chave, nunca valores.
	/// </summary>
	/// <param name="isDevelopment">Em Development a base pública é opcional (assume a local).</param>
	/// <param name="error">Primeiro problema encontrado; nulo quando válido.</param>
	internal bool TryValidate(bool isDevelopment, out string? error)
	{
		if (string.IsNullOrWhiteSpace(PublicBaseUrl))
		{
			if (!isDevelopment)
			{
				error = $"'{PublicBaseUrlKey}' é obrigatória fora de Development — o link de credencial nunca é derivado do header Host (ADR-0020).";
				return false;
			}
		}
		else if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var baseUri)
			|| (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
		{
			error = $"'{PublicBaseUrlKey}' deve ser uma URL absoluta http(s) (ex.: https://id.empresa.com).";
			return false;
		}

		return InRange(InviteLifetimeHours, 1, MaxInviteLifetimeHours, nameof(InviteLifetimeHours), out error)
			&& InRange(ResetLifetimeMinutes, 1, MaxResetLifetimeMinutes, nameof(ResetLifetimeMinutes), out error)
			&& InRange(ForgotPerAccountPerHour, 1, int.MaxValue, nameof(ForgotPerAccountPerHour), out error)
			&& InRange(ForgotPerIpPerHour, 1, int.MaxValue, nameof(ForgotPerIpPerHour), out error)
			// Piso zero é legítimo: significa "sem piso", e só faz sentido em teste.
			&& InRange(ResponseFloorMilliseconds, 0, MaxResponseFloorMilliseconds, nameof(ResponseFloorMilliseconds), out error);
	}

	private static bool InRange(int value, int min, int max, string key, out string? error)
	{
		if (value >= min && value <= max)
		{
			error = null;
			return true;
		}

		error = string.Format(
			CultureInfo.InvariantCulture,
			"'{0}:{1}' deve estar entre {2} e {3}.",
			SectionKey,
			key,
			min,
			max == int.MaxValue ? "o máximo suportado" : max.ToString(CultureInfo.InvariantCulture));

		return false;
	}
}
