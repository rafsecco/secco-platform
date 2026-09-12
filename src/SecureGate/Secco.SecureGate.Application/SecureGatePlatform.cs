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
}
