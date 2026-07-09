using Secco.SharedKernel.Exceptions;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// O tenant resolvido na requisição não existe no catálogo da plataforma —
/// tenant desativado/removido ou catálogo desatualizado no ambiente.
/// </summary>
public sealed class TenantNotFoundException : SeccoException
{
    /// <summary>Inicializa a exceção com a mensagem padrão.</summary>
    public TenantNotFoundException()
        : base("O tenant da requisição não existe no catálogo da plataforma.")
    {
    }

    /// <summary>Inicializa a exceção identificando o tenant não encontrado.</summary>
    /// <param name="tenantId">Tenant ausente do catálogo.</param>
    public TenantNotFoundException(Guid tenantId)
        : base($"O tenant '{tenantId}' não existe no catálogo da plataforma.")
    {
        TenantId = tenantId;
    }

    /// <summary>Inicializa a exceção com a mensagem informada.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    public TenantNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Inicializa a exceção com mensagem e exceção interna.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    /// <param name="innerException">Exceção que causou esta.</param>
    public TenantNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Tenant ausente do catálogo, quando conhecido.</summary>
    public Guid? TenantId { get; }
}
