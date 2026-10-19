namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>Limites da autoridade de admin de tenant (ADR-0047). POCO: a ligação à configuração é da composição.</summary>
public sealed class TenantAdminOptions
{
	/// <summary>Seção de configuração.</summary>
	public const string SectionKey = "SecureGate:TenantAdmin";

	/// <summary>Valor padrão de <see cref="MaxCreatedTenantsPerPerson"/>.</summary>
	public const int DefaultMaxCreatedTenantsPerPerson = 20;

	/// <summary>
	/// Quantos tenants uma pessoa com concessão de criação pode criar. Contém a autoexpansão: acima dele, só o
	/// operador cria. Valor fora de 1 a 1000 cai no padrão.
	/// </summary>
	public int MaxCreatedTenantsPerPerson { get; set; } = DefaultMaxCreatedTenantsPerPerson;

	/// <summary>Limite efetivo, com valores fora da faixa caindo no padrão.</summary>
	public int EffectiveMaxCreatedTenantsPerPerson =>
		MaxCreatedTenantsPerPerson is >= 1 and <= 1000 ? MaxCreatedTenantsPerPerson : DefaultMaxCreatedTenantsPerPerson;
}
