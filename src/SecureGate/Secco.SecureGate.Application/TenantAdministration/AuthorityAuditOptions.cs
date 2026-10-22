namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>
/// Retenção opt-in da trilha de autoridade guardada no SecureGate (ADR-0054). POCO: a ligação à configuração é da
/// composição. A seção é a mesma da identidade de auditoria (<c>SecureGate:Audit</c>), que lê só as chaves dela.
/// </summary>
public sealed class AuthorityAuditOptions
{
	/// <summary>Seção de configuração.</summary>
	public const string SectionKey = "SecureGate:Audit";

	/// <summary>Menor janela aceita, em dias.</summary>
	public const int MinRetentionDays = 1;

	/// <summary>Maior janela aceita, em dias (10 anos).</summary>
	public const int MaxRetentionDays = 3650;

	/// <summary>Janela de retenção em dias. Sem valor, a trilha não expira.</summary>
	public int? RetentionDays { get; set; }

	/// <summary>Há valor, mas fora de 1 a 3650: ignorado, e nada é apagado.</summary>
	public bool IsRetentionInvalid => RetentionDays is { } days && days is < MinRetentionDays or > MaxRetentionDays;

	/// <summary>Janela efetiva: nula quando a retenção está desligada ou o valor é inválido.</summary>
	public TimeSpan? EffectiveRetention =>
		RetentionDays is { } days && !IsRetentionInvalid ? TimeSpan.FromDays(days) : null;
}
