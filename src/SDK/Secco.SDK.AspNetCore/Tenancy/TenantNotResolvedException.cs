using Secco.SharedKernel.Exceptions;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Acesso a dados foi tentado sem tenant resolvido na requisição — bug de composição
/// (faltou <c>UseSeccoTenancy()</c>?) ou endpoint que deveria exigir autenticação e não exige.
/// </summary>
public sealed class TenantNotResolvedException : SeccoException
{
    /// <summary>Inicializa a exceção com a mensagem padrão.</summary>
    public TenantNotResolvedException()
        : base("Nenhum tenant foi resolvido para a requisição atual; o acesso a dados de tenant é impossível sem tenant.")
    {
    }

    /// <summary>Inicializa a exceção com a mensagem informada.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    public TenantNotResolvedException(string message)
        : base(message)
    {
    }

    /// <summary>Inicializa a exceção com mensagem e exceção interna.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    /// <param name="innerException">Exceção que causou esta.</param>
    public TenantNotResolvedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
