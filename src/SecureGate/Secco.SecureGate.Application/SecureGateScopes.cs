namespace Secco.SecureGate.Application;

/// <summary>
/// Scopes OAuth da plataforma servidos pelo SecureGate (decisão da Fase 6.3, ADR-0020/0021):
/// least privilege por construção — o client de um produto só lê o catálogo do próprio produto.
/// </summary>
public static class SecureGateScopes
{
	/// <summary>
	/// Scope administrativo do SecureGate (gestão de tenants e bancos). Concedido apenas
	/// ao AdminPortal e a operadores — nunca a clients de produto.
	/// </summary>
	public const string Admin = "securegate:admin";

	/// <summary>
	/// Scope de admin de tenant (ADR-0044): administrar, on-behalf-of a pessoa logada, SÓ os tenants em
	/// que ela tem concessão (<c>tb_tenant_admin_grants</c>), conferida a cada requisição. Concedível
	/// apenas a client de plataforma, pela configuração; no login sai só para quem tem concessão.
	/// </summary>
	public const string TenantAdmin = "securegate:tenant-admin";

	/// <summary>
	/// Scope de leitura da resolução role→permissions (ADR-0021, Fase 6.4). Único para a
	/// plataforma: permissões são configuração de acesso, não segredos — todo serviço que
	/// usa <c>AddSeccoAuthorization()</c> o recebe; a GESTÃO segue exclusiva do admin.
	/// </summary>
	public const string AuthorizationRead = "authorization:read";

	/// <summary>
	/// Leitura do diretório de pessoas do PRÓPRIO tenant do token (ADR-0045): id, e-mail, nome e situação.
	/// Concedível apenas a client de produto <c>client_credentials</c> vinculado a tenant — nunca a client
	/// interativo nem a client de plataforma, que atravessam tenant. O tenant vem sempre da claim.
	/// </summary>
	public const string DirectoryRead = "directory:read";

	/// <summary>
	/// Gestão do PRÓPRIO tenant do token por máquina (ADR-0052): um conjunto fechado de rotas administrativas
	/// (pessoas e perfis), só no tenant do <c>tenant_id</c>. Concedível apenas a client de produto
	/// <c>client_credentials</c> vinculado a tenant, nunca no tenant de plataforma, e só pelo operador.
	/// </summary>
	public const string TenantManage = "securegate:tenant-manage";

	/// <summary>Prefixo dos scopes de leitura de catálogo por produto (<c>catalog:&lt;produto&gt;</c>).</summary>
	public const string CatalogPrefix = "catalog:";

	/// <summary>Monta o scope de leitura de catálogo de um produto (ex.: <c>catalog:logstream</c>).</summary>
	/// <param name="product">Identificador do produto (kebab-case minúsculo).</param>
	public static string CatalogFor(string product) => CatalogPrefix + product;

	/// <summary>Prefixo dos scopes de publicação do catálogo de permissões por produto (<c>permissions:&lt;produto&gt;</c>, ADR-0039).</summary>
	public const string PermissionsPrefix = "permissions:";

	/// <summary>Monta o scope de publicação do catálogo de permissões de um produto (ex.: <c>permissions:logstream</c>).</summary>
	/// <param name="product">Identificador do produto (kebab-case minúsculo).</param>
	public static string PermissionsFor(string product) => PermissionsPrefix + product;

	/// <summary>
	/// Escopos concedíveis a client de PRODUTO, vinculado a tenant (ADR-0037). Lista fechada no
	/// código: produto novo entra aqui no mesmo PR que registra o escopo no seed de referência.
	/// <see cref="Admin"/>, <see cref="TenantAdmin"/>, <see cref="AuthorizationRead"/>, <c>catalog:*</c> e <c>securegate</c>
	/// são de infraestrutura — atravessam tenant por construção e só existem em client de plataforma.
	/// </summary>
	public static readonly IReadOnlyList<string> ProductScopes = ["logstream", "notificationhub"];

	/// <summary>
	/// Escopos concedíveis SÓ a client de produto <c>client_credentials</c> (ADR-0045): máquina do tenant,
	/// nunca uma aplicação em nome de pessoa. <see cref="TenantManage"/> (ADR-0052) entra aqui pelo mesmo motivo.
	/// </summary>
	public static readonly IReadOnlyList<string> ClientCredentialsOnlyProductScopes = [DirectoryRead, TenantManage];

	/// <summary>Indica se o escopo pode ser concedido a client de produto (comparação exata, ordinal).</summary>
	/// <param name="scope">Escopo candidato.</param>
	/// <param name="interactive">Se o client é interativo (<c>authorization_code</c>); os escopos só de máquina ficam de fora.</param>
	public static bool IsProductScope(string scope, bool interactive = false) =>
		ProductScopes.Contains(scope, StringComparer.Ordinal)
		|| (!interactive && ClientCredentialsOnlyProductScopes.Contains(scope, StringComparer.Ordinal));
}
