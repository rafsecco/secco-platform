namespace Secco.SecureGate.Application.Elevation;

/// <summary>Configuração da capacidade de elevação (seção <c>SecureGate:Elevation</c>, ADR-0031).</summary>
public sealed class ElevationOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "SecureGate:Elevation";

	/// <summary>
	/// TTL pedido para o token de elevação, em minutos. É um pedido e não uma ordem: o valor efetivo
	/// passa por <see cref="EffectiveTokenLifetime"/>.
	/// </summary>
	public int TokenLifetimeMinutes { get; set; } =
		(int)SecureGatePlatform.ElevatedTokenDefaultLifetime.TotalMinutes;

	/// <summary>
	/// TTL efetivo, entre 1 minuto e <see cref="SecureGatePlatform.ElevatedTokenMaxLifetime"/>
	/// (ADR-0031, invariante 3). A configuração pode APERTAR o TTL, nunca afrouxá-lo além do teto —
	/// o teto limita a janela em que um token emitido segue válido depois de a concessão ser
	/// revogada. Valor não positivo cai no piso, não no padrão: configuração inválida resulta em
	/// token mais curto, que é o lado seguro.
	/// </summary>
	public TimeSpan EffectiveTokenLifetime
	{
		get
		{
			var maxMinutes = (int)SecureGatePlatform.ElevatedTokenMaxLifetime.TotalMinutes;

			return TimeSpan.FromMinutes(Math.Clamp(TokenLifetimeMinutes, 1, maxMinutes));
		}
	}
}
