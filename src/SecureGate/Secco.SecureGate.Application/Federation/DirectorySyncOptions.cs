namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Reconciliação periódica de perfis com o diretório federado (ADR-0055, entrega A). POCO: a ligação à
/// configuração é da composição (ADR-0002). Valor fora da faixa NÃO derruba a subida — cai no padrão, e o serviço
/// avisa uma vez no log: o ciclo é uma salvaguarda, não uma dependência de partida.
/// </summary>
public sealed class DirectorySyncOptions
{
	/// <summary>Seção de configuração.</summary>
	public const string SectionKey = "SecureGate:DirectorySync";

	/// <summary>Intervalo padrão do ciclo, em horas.</summary>
	public const int DefaultIntervalHours = 24;

	/// <summary>Menor intervalo aceito, em horas.</summary>
	public const int MinIntervalHours = 1;

	/// <summary>Maior intervalo aceito, em horas (7 dias).</summary>
	public const int MaxIntervalHours = 168;

	/// <summary>Fração padrão das atribuições <c>Directory</c> do tenant que um ciclo pode retirar.</summary>
	public const double DefaultMaxRemovalRatio = 0.10;

	/// <summary>Piso padrão de retiradas abaixo do qual o freio nunca dispara (não trava tenant pequeno).</summary>
	public const int DefaultMinRemovalsBeforeBrake = 5;

	/// <summary>Intervalo do ciclo em horas (<c>IntervalHours</c>). Sem valor ou fora de 1 a 168, vale o padrão.</summary>
	public int? IntervalHours { get; set; }

	/// <summary>Fração máxima de retiradas (<c>MaxRemovalRatio</c>), de 0 a 1. Sem valor ou fora da faixa, vale o padrão.</summary>
	public double? MaxRemovalRatio { get; set; }

	/// <summary>Piso do freio (<c>MinRemovalsBeforeBrake</c>), a partir de 0. Sem valor ou negativo, vale o padrão.</summary>
	public int? MinRemovalsBeforeBrake { get; set; }

	/// <summary>Há valor de intervalo, mas fora da faixa válida.</summary>
	public bool IsIntervalInvalid => IntervalHours is { } hours && hours is < MinIntervalHours or > MaxIntervalHours;

	/// <summary>Há valor de fração, mas fora de 0 a 1 (ou não numérico).</summary>
	public bool IsRatioInvalid => MaxRemovalRatio is { } ratio && !(ratio >= 0 && ratio <= 1);

	/// <summary>Há valor de piso, mas negativo.</summary>
	public bool IsMinRemovalsInvalid => MinRemovalsBeforeBrake is < 0;

	/// <summary>Intervalo efetivo do ciclo.</summary>
	public TimeSpan EffectiveInterval =>
		TimeSpan.FromHours(IntervalHours is { } hours && !IsIntervalInvalid ? hours : DefaultIntervalHours);

	/// <summary>Fração efetiva do freio de massa.</summary>
	public double EffectiveMaxRemovalRatio =>
		MaxRemovalRatio is { } ratio && !IsRatioInvalid ? ratio : DefaultMaxRemovalRatio;

	/// <summary>Piso efetivo do freio de massa.</summary>
	public int EffectiveMinRemovalsBeforeBrake =>
		MinRemovalsBeforeBrake is { } min && !IsMinRemovalsInvalid ? min : DefaultMinRemovalsBeforeBrake;
}
