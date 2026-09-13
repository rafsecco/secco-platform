namespace Secco.SecureGate.Application;

/// <summary>
/// Constantes do modelo de OPERADOR de instalação (ADR-0023/vocabulário renomeado pela issue
/// #4/2026-09): operadores do AdminPortal são usuários com o role <see cref="OperatorRole"/>
/// num tenant de plataforma bem-conhecido. A instalação pertence a quem ADOTOU a plataforma,
/// não à Secco — cada instalação é soberana; o operador é operador DESTA instalação.
/// O scope <c>securegate:admin</c> só é emitido a esses usuários no login (filtro no
/// <c>/connect/authorize</c>) — login de usuário comum não escala para admin (ADR-0020).
/// </summary>
public static class SecureGatePlatform
{
	/// <summary>Tenant de plataforma (Guid fixo) que hospeda os usuários operadores.</summary>
	public static readonly Guid TenantId = Guid.Parse("018f0000-0000-7000-8000-0000000000ff");

	/// <summary>Slug do tenant de plataforma.</summary>
	public const string TenantSlug = "instalacao";

	/// <summary>
	/// Nome de exibição do tenant de plataforma. "Instalação" e não "Plataforma Secco"
	/// (issue #4/2026-09): a instalação é da empresa que adotou a plataforma, não da Secco —
	/// o tenant é localizado por <see cref="TenantId"/> (Guid fixo), nome e slug são só display.
	/// </summary>
	public const string TenantName = "Instalação";

	/// <summary>Role que marca um usuário como operador de instalação (gate do scope admin).</summary>
	public const string OperatorRole = "installation-operator";

	/// <summary>
	/// Nome ANTIGO do role de operador (issue #4/2026-09): "platform-operator" induzia ao erro
	/// de sugerir um operador da Secco, quando na verdade é o operador desta instalação. Existe
	/// SÓ para a convergência idempotente do seed de referência — uma instalação anterior tem
	/// esse nome gravado em <c>tb_roles</c>, e o seed o renomeia no lugar (preservando o Id, para
	/// que as atribuições em <c>tb_user_roles</c> sobrevivam ao rename). Também reservado em
	/// <c>RoleInputRules.IsReservedName</c> pelo mesmo motivo. Candidata a sair quando não
	/// houver mais nenhuma instalação com o nome antigo gravado.
	/// </summary>
	public const string LegacyOperatorRole = "platform-operator";

	/// <summary>
	/// Conjunto READ-ONLY que o operador de instalação recebe em QUALQUER tenant (ADR-0024) —
	/// política de IAM sobre o que o operador pode ler (exceção consciente à ADR-0003:
	/// referencia nomes de permissão de produto, mas aqui é política, não a constante do produto).
	/// Somente leitura por princípio: o operador inspeciona, não escreve em tenant alheio.
	/// </summary>
	public static readonly IReadOnlyList<string> OperatorReadPermissions =
	[
		"log-entries:read",
		"log-processes:read",
		"api-call-logs:read",

		// A trilha de auditoria (issue #2) é recurso de log e segue a mesma postura já decidida
		// na ADR-0024: o operador lê a de qualquer tenant. Escolha consciente, não herança
		// automática — auditoria é mais sensível que diagnóstico, e fora do read-set ela
		// simplesmente não apareceria no AdminPortal.
		"audit-entries:read",
	];

	/// <summary>
	/// Papel carimbado pelo emissor no token de ELEVAÇÃO (ADR-0031). Nunca é persistido em
	/// <c>tb_roles</c> nem atribuível pela gestão, e seu nome é reservado. Não reusa
	/// <see cref="OperatorRole"/> de propósito: o token afirmaria ser o que o portador não é, e a
	/// trilha de auditoria não distinguiria um leitor elevado de um operador real.
	/// </summary>
	/// <remarks>
	/// O alcance cross-tenant NÃO vem deste papel — vem de o token elevado não carregar
	/// <c>tenant_id</c>. Um token comum que, por colisão de nome, trouxesse este papel continuaria
	/// carregando o <c>tenant_id</c> do usuário, e a regra de conflito da ADR-0005 (claim vence,
	/// header divergente = 400) o prenderia ao próprio tenant. Há teste provando isso.
	/// </remarks>
	public const string ElevatedLogReaderRole = "installation-log-reader";

	/// <summary>
	/// Papel da identidade de serviço do SecureGate que grava a auditoria das trocas
	/// (ADR-0031, emenda de 2026-09-13). Vive só no <c>ds_roles</c> de um client OIDC semeado —
	/// papel de client não é gerido por API —, e seu nome é reservado.
	/// </summary>
	public const string AuditorRole = "installation-auditor";

	/// <summary>
	/// Read-set do leitor elevado (ADR-0031). Listado EXPLICITAMENTE, e não derivado de
	/// <see cref="OperatorReadPermissions"/>: uma leitura acrescentada ao operador no futuro não
	/// pode fluir em silêncio para quem só elevou. Deliberadamente SEM <c>audit-entries:read</c> —
	/// o caso de uso é diagnóstico, e a trilha de auditoria é dado mais sensível que o diagnóstico.
	/// </summary>
	public static readonly IReadOnlyList<string> ElevatedLogReaderPermissions =
	[
		"log-entries:read",
		"log-processes:read",
		"api-call-logs:read",
	];

	/// <summary>
	/// Única permissão cross-tenant de ESCRITA da plataforma (ADR-0031, emenda de 2026-09-13) —
	/// a "nova ADR" que a ADR-0024 exige para escrita cross-tenant, com escopo mínimo: uma
	/// permissão, uma identidade, um propósito. Nenhuma leitura. Seguro porque o SecureGate já é a
	/// raiz de confiança: quem o compromete emite token para qualquer um, então escrever auditoria
	/// não amplia o que um atacante nele consegue fazer.
	/// </summary>
	public static readonly IReadOnlyList<string> AuditorPermissions =
	[
		"audit-entries:write",
	];

	/// <summary>TTL padrão do token de elevação (ADR-0031).</summary>
	public static readonly TimeSpan ElevatedTokenDefaultLifetime = TimeSpan.FromMinutes(15);

	/// <summary>
	/// Teto do TTL do token de elevação (ADR-0031). A configuração pode APERTAR o TTL, nunca
	/// afrouxá-lo além deste valor — o teto limita a janela em que um token emitido segue válido
	/// depois de a concessão ser revogada.
	/// </summary>
	public static readonly TimeSpan ElevatedTokenMaxLifetime = TimeSpan.FromMinutes(60);

	/// <summary>
	/// Único escopo emitível pela capacidade de elevação (ADR-0031, invariante 1). Qualquer outro
	/// escopo pedido na troca faz a troca ser recusada — nunca estreitado em silêncio, para que o
	/// uso indevido apareça em vez de ser mascarado.
	/// </summary>
	public const string ElevatedScope = "logstream";

	/// <summary>
	/// Claim que marca um token como produto de troca (ADR-0031, invariante 4), com o nome da
	/// capacidade como valor. É o que torna o token NÃO re-trocável: sem ele, um token elevado
	/// poderia ser trocado de novo pelo mesmo usuário e renovar o próprio TTL indefinidamente.
	/// Fica no SecureGate e não no SharedKernel (ADR-0003): só o emissor o lê.
	/// </summary>
	public const string TokenExchangeClaim = "token_exchange";

	/// <summary>Valor de <see cref="TokenExchangeClaim"/> para a capacidade de elevação (ADR-0031).</summary>
	public const string ElevationCapability = "elevation";
}
