namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Constantes do AdminPortal. Os valores específicos do SecureGate (role de operador, scopes)
/// espelham `SecureGatePlatform`/`SecureGateScopes` do SecureGate — não são referenciados
/// diretamente porque o AdminPortal só depende do <c>Secco.SecureGate.Client</c> (ADR-0006),
/// não do projeto Application do produto.
/// </summary>
public static class AdminPortalDefaults
{
    /// <summary>Role que marca o operador de plataforma (ADR-0023) — gate das telas de gestão.</summary>
    public const string OperatorRole = "platform-operator";

    /// <summary>Policy que exige o role de operador.</summary>
    public const string OperatorPolicy = "Operator";

    /// <summary>Nome do <c>HttpClient</c> tipado do SecureGate.</summary>
    public const string SecureGateHttpClient = "SecureGate";

    /// <summary>Nome do <c>HttpClient</c> tipado do LogStream (leitura de logs, Fase 7.3).</summary>
    public const string LogStreamHttpClient = "LogStream";

    /// <summary>Claim onde o access token do operador é custodiado no principal do cookie (ADR-0023).</summary>
    public const string AccessTokenClaim = "access_token";

    /// <summary>Scopes solicitados no login do operador (on-behalf-of das APIs de produto).</summary>
    public static readonly string[] Scopes =
        ["openid", "profile", "email", "roles", "offline_access", "securegate:admin", "logstream"];
}
