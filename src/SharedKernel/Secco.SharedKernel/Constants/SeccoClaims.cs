namespace Secco.SharedKernel.Constants;

/// <summary>
/// Claims padronizadas dos tokens emitidos pelo Secco.SecureGate (ADR-0007).
/// </summary>
public static class SeccoClaims
{
    /// <summary>Identificador do usuário (subject).</summary>
    public const string Subject = "sub";

    /// <summary>Identificador do tenant do usuário (ADR-0005).</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Role (perfil) do usuário — a autorização granular resolve permissões a partir dele (ADR-0021).</summary>
    public const string Role = "role";

    /// <summary>Escopos concedidos ao token.</summary>
    public const string Scope = "scope";
}
