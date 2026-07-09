namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Tenant da requisição atual (ADR-0005), populado por <see cref="SeccoTenancyMiddleware"/>.
/// Registrado com tempo de vida <c>Scoped</c>. Pode não estar resolvido — endpoints
/// públicos e health checks funcionam sem tenant; a barreira de isolamento é o
/// <see cref="ITenantConnectionFactory"/>, que falha sem tenant resolvido.
/// </summary>
public interface ITenantContext
{
    /// <summary>Tenant resolvido para a requisição atual; nulo se nenhuma fonte o resolveu.</summary>
    Guid? TenantId { get; }

    /// <summary>Indica se algum tenant foi resolvido para a requisição atual.</summary>
    bool IsResolved { get; }
}
