namespace Secco.SecureGate.Application;

/// <summary>
/// Constantes do modelo de OPERADOR de plataforma (ADR-0023): operadores do AdminPortal são
/// usuários com o role <see cref="OperatorRole"/> num tenant de plataforma bem-conhecido.
/// O scope <c>securegate:admin</c> só é emitido a esses usuários no login (filtro no
/// <c>/connect/authorize</c>) — login de usuário comum não escala para admin (ADR-0020).
/// </summary>
public static class SecureGatePlatform
{
    /// <summary>Tenant de plataforma (Guid fixo) que hospeda os usuários operadores.</summary>
    public static readonly Guid TenantId = Guid.Parse("018f0000-0000-7000-8000-0000000000ff");

    /// <summary>Slug do tenant de plataforma.</summary>
    public const string TenantSlug = "plataforma";

    /// <summary>Nome de exibição do tenant de plataforma.</summary>
    public const string TenantName = "Plataforma Secco";

    /// <summary>Role que marca um usuário como operador de plataforma (gate do scope admin).</summary>
    public const string OperatorRole = "platform-operator";
}
