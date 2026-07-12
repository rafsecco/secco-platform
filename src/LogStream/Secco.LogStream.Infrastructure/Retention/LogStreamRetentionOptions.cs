namespace Secco.LogStream.Infrastructure.Retention;

/// <summary>
/// Política de retenção (seção <c>LogStream:Retention</c>). <b>Opt-in explícito</b>:
/// sem <see cref="DefaultDays"/> e sem overrides, o worker fica inativo — apagar dados
/// jamais é efeito colateral de default. Janela única para os três tipos de log;
/// override por tenant para contratos com retenção diferente.
/// </summary>
public sealed class LogStreamRetentionOptions
{
    /// <summary>Dias de retenção default para todos os tenants; nulo = retenção inativa.</summary>
    public int? DefaultDays { get; set; }

    /// <summary>Intervalo entre execuções do expurgo, em horas (default 6).</summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>Override de dias por tenant (vence o <see cref="DefaultDays"/>).</summary>
    public Dictionary<Guid, int> DaysByTenant { get; } = [];
}

/// <summary>Resolução da janela de retenção efetiva de um tenant.</summary>
internal static class RetentionPolicy
{
    /// <summary>
    /// Dias de retenção do tenant: override específico vence o default; nulo = não expurgar.
    /// </summary>
    /// <param name="options">Política configurada.</param>
    /// <param name="tenantId">Tenant em processamento.</param>
    public static int? ResolveDays(LogStreamRetentionOptions options, Guid tenantId) =>
        options.DaysByTenant.TryGetValue(tenantId, out var tenantDays)
            ? tenantDays
            : options.DefaultDays;

    /// <summary>Config válida: dias positivos onde definidos e intervalo de ao menos 1 hora.</summary>
    /// <param name="options">Política configurada.</param>
    public static bool IsValid(LogStreamRetentionOptions options) =>
        options.IntervalHours >= 1
        && options.DefaultDays is null or > 0
        && options.DaysByTenant.Values.All(days => days > 0);
}
