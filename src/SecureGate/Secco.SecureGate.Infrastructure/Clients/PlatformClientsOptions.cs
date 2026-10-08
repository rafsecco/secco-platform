namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>Tipo de client de plataforma declarável (ADR-0037). Client público não existe nesta versão.</summary>
public enum PlatformClientType
{
	/// <summary>Máquina: grant client credentials.</summary>
	ClientCredentials,

	/// <summary>Relying party confidencial: authorization code + PKCE + refresh, consent implícito.</summary>
	AuthorizationCode,
}

/// <summary>Um client de plataforma declarado na configuração (ADR-0037).</summary>
public sealed class PlatformClientDefinition
{
	/// <summary>Identificador kebab-case; nunca com o prefixo <c>cli_</c> da API.</summary>
	public string? ClientId { get; set; }

	/// <summary>Tipo do client.</summary>
	public PlatformClientType? Type { get; set; }

	/// <summary>Secret — de variável de ambiente ou cofre, nunca de arquivo versionado.</summary>
	public string? ClientSecret { get; set; }

	/// <summary>Escopos concedidos (precisam estar registrados).</summary>
	public List<string> Scopes { get; set; } = [];

	/// <summary>Papéis carregados na claim <c>role</c>.</summary>
	public List<string> Roles { get; set; } = [];

	/// <summary>Redirect URIs (só <see cref="PlatformClientType.AuthorizationCode"/>).</summary>
	public List<string> RedirectUris { get; set; } = [];

	/// <summary>Post-logout redirect URIs (só <see cref="PlatformClientType.AuthorizationCode"/>).</summary>
	public List<string> PostLogoutRedirectUris { get; set; } = [];
}

/// <summary>
/// Clients de plataforma (seção <c>SecureGate:PlatformClients</c>, ADR-0037): a configuração é a
/// fonte da verdade, reconciliada pelo seed de referência.
/// </summary>
public sealed class PlatformClientsOptions
{
	/// <summary>Seção que contém a lista <c>PlatformClients</c>.</summary>
	public const string SectionKey = "SecureGate";

	/// <summary>Clients declarados.</summary>
	public List<PlatformClientDefinition> PlatformClients { get; set; } = [];
}
