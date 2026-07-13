using OpenIddict.EntityFrameworkCore.Models;

namespace Secco.SecureGate.Infrastructure.OpenIddict;

/// <summary>
/// Entidades do OpenIddict com chaves Guid e <b>nomes curtos</b> (mesmo motivo das do
/// Identity: a convention deriva nomes das classes — <c>OidcApplication</c> →
/// <c>id_pk_oidc_application</c>, e não o nome gigante dos tipos default da biblioteca).
/// </summary>
public sealed class OidcApplication : OpenIddictEntityFrameworkCoreApplication<Guid, OidcAuthorization, OidcToken>
{
    /// <summary>Tamanho máximo da lista de roles do client.</summary>
    public const int RolesMaxLength = 500;

    /// <summary>
    /// Roles do client, separados por espaço (coluna <c>ds_roles</c>) — emitidos como
    /// claim curta <c>role</c> em tokens client credentials (Fase 6.4): máquinas entram
    /// no MESMO modelo Role + Permission da ADR-0021 que os usuários, sem caso especial.
    /// </summary>
    public string? Roles { get; set; }
}

/// <summary>Autorização concedida (tabela <c>tb_oidc_authorizations</c>).</summary>
public sealed class OidcAuthorization : OpenIddictEntityFrameworkCoreAuthorization<Guid, OidcApplication, OidcToken>;

/// <summary>Escopo registrado (tabela <c>tb_oidc_scopes</c>).</summary>
public sealed class OidcScope : OpenIddictEntityFrameworkCoreScope<Guid>;

/// <summary>Token emitido/rastreado (tabela <c>tb_oidc_tokens</c>).</summary>
public sealed class OidcToken : OpenIddictEntityFrameworkCoreToken<Guid, OidcApplication, OidcAuthorization>;
