namespace Secco.SecureGate.Application.InstallationOperators;

/// <summary>
/// Operadores da instalação por designação (seção <c>SecureGate:InstallationOperators</c>, ADR-0049): contas de
/// outro tenant que a INSTALAÇÃO declara operadoras. A configuração é a única fonte — nenhuma API designa ninguém,
/// e tirar a conta da lista revoga na requisição seguinte. POCO: a ligação à configuração é da composição.
/// </summary>
public sealed class InstallationOperatorOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "SecureGate:InstallationOperators";

	/// <summary>
	/// Chave do <see cref="TimeProvider"/> que mede a idade da autenticação na troca. Relógio próprio, e não o do
	/// contêiner: o OpenIddict e os cookies leem o do contêiner, e mexer nele para testar "autenticação antiga"
	/// mudaria a validade de todos os tokens.
	/// </summary>
	public const string ClockKey = "secco:installation-operators:clock";

	/// <summary>Teto do TTL do token de operador designado, e da janela de autenticação recente.</summary>
	public static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(15);

	/// <summary>Valor padrão do TTL e da janela de autenticação recente, em minutos.</summary>
	public const int DefaultMinutes = 10;

	/// <summary>
	/// Contas designadas, pelo ID (imutável). E-mail não serve: o dono troca o próprio e-mail (ADR-0033), e a
	/// designação não pode seguir um endereço.
	/// </summary>
	public IList<Guid> Accounts { get; } = [];

	/// <summary>TTL pedido para o token de operador designado, em minutos (ver <see cref="EffectiveTokenLifetime"/>).</summary>
	public int TokenLifetimeMinutes { get; set; } = DefaultMinutes;

	/// <summary>
	/// Há quanto tempo, no máximo, a pessoa pode ter se autenticado com segundo fator para trocar o token (ver
	/// <see cref="EffectiveRecentAuthentication"/>).
	/// </summary>
	public int RecentAuthenticationMinutes { get; set; } = DefaultMinutes;

	/// <summary>
	/// TTL efetivo, entre 1 minuto e <see cref="MaxWindow"/>: a configuração aperta, nunca afrouxa. Valor não
	/// positivo cai no piso — o lado seguro.
	/// </summary>
	public TimeSpan EffectiveTokenLifetime => Clamp(TokenLifetimeMinutes);

	/// <summary>Janela efetiva de autenticação recente, entre 1 minuto e <see cref="MaxWindow"/>.</summary>
	public TimeSpan EffectiveRecentAuthentication => Clamp(RecentAuthenticationMinutes);

	private static TimeSpan Clamp(int minutes) =>
		TimeSpan.FromMinutes(Math.Clamp(minutes, 1, (int)MaxWindow.TotalMinutes));
}

/// <summary>
/// Quem a instalação designou operador (ADR-0049). Porta e não leitura direta das opções: a designação é consultada
/// a cada requisição com o token de operador designado, e revogar tem de valer na seguinte.
/// </summary>
public interface IInstallationOperatorDesignations
{
	/// <summary>Indica se a conta é operadora designada agora.</summary>
	/// <param name="userId">Conta.</param>
	bool IsDesignated(Guid userId);

	/// <summary>As contas designadas agora.</summary>
	IReadOnlyCollection<Guid> Current { get; }
}
