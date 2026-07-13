using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application;

/// <summary>Erros de negócio do SecureGate (ADR-0004): códigos estáveis <c>SecureGate.*</c>.</summary>
public static class SecureGateErrors
{
    /// <summary>Erros do catálogo de tenants.</summary>
    public static class Tenants
    {
        /// <summary>Tenant não encontrado no catálogo.</summary>
        public static readonly Error NotFound =
            Error.NotFound("SecureGate.Tenant.NotFound", "Tenant não encontrado.");
    }
}
