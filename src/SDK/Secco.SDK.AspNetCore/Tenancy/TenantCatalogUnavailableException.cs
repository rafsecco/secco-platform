using Secco.SharedKernel.Exceptions;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// O catálogo central de tenants está temporariamente inacessível e não há entrada em
/// cache utilizável para atender a requisição — condição transitória de infraestrutura
/// (o SecureGate fora do ar, por exemplo), convertida em <c>503 + Retry-After</c> pelo
/// pipeline de tenancy. Diferente de <see cref="TenantNotFoundException"/>: aqui não se
/// sabe se o tenant existe.
/// </summary>
public sealed class TenantCatalogUnavailableException : SeccoException
{
    /// <summary>Inicializa a exceção com a mensagem padrão.</summary>
    public TenantCatalogUnavailableException()
        : base("O catálogo de tenants da plataforma está temporariamente indisponível.")
    {
    }

    /// <summary>Inicializa a exceção com a mensagem informada.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    public TenantCatalogUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Inicializa a exceção com mensagem e exceção interna.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    /// <param name="innerException">Exceção que causou esta.</param>
    public TenantCatalogUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
