namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Configuração da app registration multi-tenant da plataforma no Entra ID (ADR-0026,
/// seção <c>SecureGate:EntraId</c>). Uma ÚNICA app do adotante serve todos os tenants:
/// cada empresa cliente consente a app no próprio diretório (admin consent) e registra
/// seu directory id via <c>PUT /api/v1/tenants/{id}/federation</c>. Sem <see cref="ClientId"/>
/// e <see cref="ClientSecret"/>, o login federado fica desligado (botão não aparece).
/// </summary>
public sealed class SecureGateEntraIdOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "SecureGate:EntraId";

    /// <summary>Nome do esquema de autenticação OIDC do login federado.</summary>
    public const string AuthenticationScheme = "EntraId";

    /// <summary>Client id da app registration multi-tenant da plataforma.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret da app registration. Segredo — nunca aparece em logs ou respostas (ADR-0020).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Authority OIDC do Entra ID. O default <c>organizations</c> aceita qualquer diretório
    /// corporativo — o pin real por tenant é o directory id registrado, verificado no login (ADR-0026).
    /// </summary>
    public string Authority { get; set; } = "https://login.microsoftonline.com/organizations/v2.0";

    /// <summary>Login federado configurado (client id e secret presentes).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
