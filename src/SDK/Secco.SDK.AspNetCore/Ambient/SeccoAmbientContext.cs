namespace Secco.SDK.AspNetCore.Ambient;

/// <summary>
/// Espelho <b>ambiente</b> (<see cref="AsyncLocal{T}"/>) do tenant e da correlação da unidade de
/// trabalho corrente — requisição HTTP ou escopo de job (ADR-0004/0005).
/// </summary>
/// <remarks>
/// Existe por um motivo único: <c>ITenantContext</c> e <c>ICorrelationContext</c> são
/// <c>Scoped</c>, e há consumidores legítimos que são singleton por natureza e portanto não
/// conseguem resolvê-los — o caso concreto é um <c>ILoggerProvider</c>, que o runtime cria uma
/// vez para o processo inteiro e que precisa saber, a cada chamada de log, em qual tenant e em
/// qual requisição ele está. Dentro de um escopo HTTP não bastaria <c>IHttpContextAccessor</c>:
/// um job do Hangfire tem escopo de DI mas não tem <c>HttpContext</c>.
/// <para>
/// Código de aplicação continua consumindo os contextos <c>Scoped</c> — este tipo não os
/// substitui e não é o caminho recomendado para regra de negócio. Quem escreve os valores é o
/// próprio SDK: os middlewares de correlação e tenancy, e <c>TenantScopeExtensions.SetTenant</c>.
/// </para>
/// </remarks>
public static class SeccoAmbientContext
{
	private static readonly AsyncLocal<Guid?> AmbientTenantId = new();

	private static readonly AsyncLocal<string?> AmbientCorrelationId = new();

	/// <summary>Tenant da unidade de trabalho corrente; nulo fora de escopo com tenant resolvido.</summary>
	public static Guid? TenantId => AmbientTenantId.Value;

	/// <summary>Correlation id da unidade de trabalho corrente; nulo fora do pipeline.</summary>
	public static string? CorrelationId => AmbientCorrelationId.Value;

	/// <summary>Define o tenant ambiente. Interno: só o SDK escreve, o resto do mundo lê.</summary>
	/// <param name="tenantId">Tenant resolvido, ou nulo quando não há.</param>
	internal static void SetTenantId(Guid? tenantId) => AmbientTenantId.Value = tenantId;

	/// <summary>Define a correlação ambiente. Interno: só o SDK escreve, o resto do mundo lê.</summary>
	/// <param name="correlationId">Correlation id da unidade de trabalho corrente.</param>
	internal static void SetCorrelationId(string? correlationId) => AmbientCorrelationId.Value = correlationId;
}
