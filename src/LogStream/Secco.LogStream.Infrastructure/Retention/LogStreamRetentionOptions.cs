namespace Secco.LogStream.Infrastructure.Retention;

/// <summary>
/// Política de retenção (seção <c>LogStream:Retention</c>). <b>Opt-in explícito</b>:
/// sem <see cref="DefaultDays"/>/<see cref="AuditDefaultDays"/> e sem overrides, o worker
/// fica inativo — apagar dados jamais é efeito colateral de default. Duas janelas
/// independentes por classe de dado: diagnóstico (<see cref="DefaultDays"/>/<see cref="DaysByTenant"/>,
/// como antes) e auditoria (<see cref="AuditDefaultDays"/>/<see cref="AuditDaysByTenant"/>) —
/// a janela de auditoria é <c>null</c> por padrão, e isso é intencional: sem configuração
/// explícita, a trilha de auditoria nunca expira. A janela de diagnóstico jamais leva a
/// trilha de auditoria junto, mesmo se configurada sozinha.
/// </summary>
public sealed class LogStreamRetentionOptions
{
	/// <summary>Dias de retenção de diagnóstico (log geral, processos, chamadas de API) default para todos os tenants; nulo = inativa.</summary>
	public int? DefaultDays { get; set; }

	/// <summary>Intervalo entre execuções do expurgo, em horas (default 6).</summary>
	public int IntervalHours { get; set; } = 6;

	/// <summary>Override de dias de retenção de diagnóstico por tenant (vence o <see cref="DefaultDays"/>).</summary>
	public Dictionary<Guid, int> DaysByTenant { get; } = [];

	/// <summary>
	/// Dias de retenção da trilha de auditoria default para todos os tenants; nulo (default)
	/// = auditoria nunca expira. Classe de dado distinta do diagnóstico — obrigação legal,
	/// não conveniência de troubleshooting.
	/// </summary>
	public int? AuditDefaultDays { get; set; }

	/// <summary>Override de dias de retenção de auditoria por tenant (vence o <see cref="AuditDefaultDays"/>).</summary>
	public Dictionary<Guid, int> AuditDaysByTenant { get; } = [];
}

/// <summary>Resolução das janelas de retenção efetivas de um tenant, por classe de dado.</summary>
internal static class RetentionPolicy
{
	/// <summary>
	/// Dias de retenção de diagnóstico do tenant: override específico vence o default;
	/// nulo = não expurgar log geral/processos/chamadas de API.
	/// </summary>
	/// <param name="options">Política configurada.</param>
	/// <param name="tenantId">Tenant em processamento.</param>
	public static int? ResolveDays(LogStreamRetentionOptions options, Guid tenantId) =>
		options.DaysByTenant.TryGetValue(tenantId, out var tenantDays)
			? tenantDays
			: options.DefaultDays;

	/// <summary>
	/// Dias de retenção de auditoria do tenant: override específico vence o default;
	/// nulo = a trilha de auditoria nunca expira (garantia default da plataforma).
	/// </summary>
	/// <param name="options">Política configurada.</param>
	/// <param name="tenantId">Tenant em processamento.</param>
	public static int? ResolveAuditDays(LogStreamRetentionOptions options, Guid tenantId) =>
		options.AuditDaysByTenant.TryGetValue(tenantId, out var tenantDays)
			? tenantDays
			: options.AuditDefaultDays;

	/// <summary>Config válida: dias positivos onde definidos (nas duas classes de dado) e intervalo de ao menos 1 hora.</summary>
	/// <param name="options">Política configurada.</param>
	public static bool IsValid(LogStreamRetentionOptions options) =>
		options.IntervalHours >= 1
		&& options.DefaultDays is null or > 0
		&& options.DaysByTenant.Values.All(days => days > 0)
		&& options.AuditDefaultDays is null or > 0
		&& options.AuditDaysByTenant.Values.All(days => days > 0);
}
