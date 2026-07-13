namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>
/// Resolve as permissões (<c>recurso:acao</c>, ADR-0021) de um role em um tenant.
/// A implementação padrão lê de <c>IConfiguration</c> (<see cref="ConfigurationPermissionResolver"/>,
/// DEV/testes); em produção o <c>Secco.SecureGate.Client</c> registra a resolução remota
/// no SecureGate. O consumo pelo pipeline SEMPRE passa pelo cache obrigatório da plataforma
/// (chave <c>(tenant_id, role)</c>, TTL curto) — implementações não precisam cachear.
/// </summary>
public interface IPermissionResolver
{
    /// <summary>Resolve o conjunto de permissões de um role no tenant.</summary>
    /// <param name="tenantId">Tenant do contexto da requisição.</param>
    /// <param name="role">Nome do role (claim curta <c>role</c>, ADR-0007).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Permissões concedidas; vazio se o role não existe ou nada concede.</returns>
    ValueTask<IReadOnlySet<string>> ResolveAsync(Guid tenantId, string role, CancellationToken cancellationToken = default);
}
