namespace Secco.AdminPortal.Services;

/// <summary>Gestão de tenants a partir do AdminPortal, on-behalf-of o operador (ADR-0023).</summary>
public interface ITenantAdminService
{
	/// <summary>Lista os tenants do catálogo.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Cria um tenant no catálogo (nasce ativo, sem bancos). Pelo operador, sem concessão de admin para
	/// ninguém (ADR-0051). Nome e slug são validados pelo SecureGate; slug repetido responde 409.
	/// </summary>
	/// <param name="name">Nome de exibição.</param>
	/// <param name="slug">Identificador curto único (kebab-case).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantSummary> CreateTenantAsync(string name, string slug, CancellationToken cancellationToken = default);

	/// <summary>Busca o detalhe de um tenant (cabeçalho da tela de gestão).</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantDetail> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Cadastra ou rotaciona o banco de um tenant num produto (PUT idempotente, Fase 7.4).
	/// Write-only: a connection string nunca volta em nenhuma leitura (ADR-0020).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="product">Produto (kebab-case).</param>
	/// <param name="connectionString">Connection string do banco dedicado. Nunca logar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task UpsertDatabaseAsync(
		Guid tenantId, string product, string connectionString, CancellationToken cancellationToken = default);

	/// <summary>
	/// Configura a federação Entra ID do tenant (PUT idempotente, ADR-0026). Federação é só do operador (ADR-0052).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="directoryId">Diretório Entra (<c>tid</c>) do tenant.</param>
	/// <param name="enabled">Se o login federado fica habilitado.</param>
	/// <param name="allowedEmailDomains">
	/// Domínios de e-mail permitidos ao provisionamento pelo diretório (ADR-0055). <c>null</c> mantém a lista atual;
	/// lista vazia a esvazia.
	/// </param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SetFederationAsync(
		Guid tenantId,
		Guid directoryId,
		bool enabled,
		IReadOnlyList<string>? allowedEmailDomains = null,
		CancellationToken cancellationToken = default);

	/// <summary>Lista os mapeamentos grupo → perfil do tenant (ADR-0036), com a marca do grupo de provisionamento.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<GroupMappingSummary>> ListGroupMappingsAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Marca ou desmarca o grupo do mapeamento como o de provisionamento do tenant (ADR-0055). Marcar passa ao
	/// diretório a decisão de quem tem conta; desmarcar a devolve. Idempotente nos dois sentidos.
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="mappingId">Mapeamento.</param>
	/// <param name="provisioning"><c>true</c> para marcar; <c>false</c> para desmarcar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SetProvisioningGroupAsync(
		Guid tenantId, Guid mappingId, bool provisioning, CancellationToken cancellationToken = default);

	/// <summary>Lista as pessoas com concessão de admin do tenant (ADR-0044).</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantAdminSummary>> ListAdminsAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Os tenants que a pessoa administra. Não há rota "por pessoa" para o operador: consulta os admins de
	/// cada tenant do catálogo — custo proporcional ao número de tenants, aceitável numa tela de gestão.
	/// </summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantSummary>> ListAdministeredByAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Concede a autoridade de admin do tenant à pessoa (PUT idempotente, ADR-0044).</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa, de qualquer tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task GrantAdminAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Revoga a autoridade de admin do tenant (DELETE idempotente; vale na próxima requisição da pessoa).</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RevokeAdminAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lê, paginada e do mais recente para o mais antigo, a trilha de autoridade do tenant (ADR-0054): o que as
	/// aplicações com <c>securegate:tenant-manage</c> escreveram nele. Só o operador lê.
	/// </summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="page">Página (1-based).</param>
	/// <param name="size">Itens por página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<AuthorityTrailPage> ListAuthorityTrailAsync(
		Guid tenantId, int page, int size, CancellationToken cancellationToken = default);
}
