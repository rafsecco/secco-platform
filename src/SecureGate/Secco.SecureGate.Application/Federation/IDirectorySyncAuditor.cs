namespace Secco.SecureGate.Application.Federation;

/// <summary>Ações da trilha da sincronização com o diretório (ADR-0055), com o prefixo da trilha de autoridade.</summary>
public static class DirectorySyncActions
{
	/// <summary>Prefixo das ações desta trilha.</summary>
	public const string Prefix = "sincronizacao-do-diretorio.";

	/// <summary>Tipo de ator desta trilha.</summary>
	public const string ActorType = "directory";

	/// <summary>Perfil acrescentado a uma conta.</summary>
	public const string RoleAdded = Prefix + "perfil-acrescentado";

	/// <summary>Perfil retirado de uma conta.</summary>
	public const string RoleRemoved = Prefix + "perfil-retirado";

	/// <summary>Retirada barrada pela guarda do último membro ativo.</summary>
	public const string RemovalBlocked = Prefix + "retirada-barrada";

	/// <summary>Ciclo abortado pelo freio de massa.</summary>
	public const string CycleAborted = Prefix + "ciclo-abortado";

	/// <summary>Leitura do Graph falhou ou veio incompleta; nada mudou.</summary>
	public const string ReadFailed = Prefix + "leitura-falhou";

	/// <summary>Conta criada a partir de uma pessoa do grupo de provisionamento (entrega B).</summary>
	public const string AccountCreated = Prefix + "conta-criada";

	/// <summary>Conta pré-existente passou a ser gerida pelo diretório (adoção).</summary>
	public const string AccountAdopted = Prefix + "conta-adotada";

	/// <summary>Conta gerida desativada por ter saído do grupo de provisionamento.</summary>
	public const string AccountDeactivated = Prefix + "conta-desativada";

	/// <summary>Conta gerida reativada por ter voltado ao grupo de provisionamento.</summary>
	public const string AccountReactivated = Prefix + "conta-reativada";

	/// <summary>Desativação barrada (conta administrativa ou último membro ativo de um perfil).</summary>
	public const string DeactivationBlocked = Prefix + "desativacao-barrada";

	/// <summary>Pessoa do diretório recusada: sem e-mail utilizável, domínio fora da lista ou e-mail já em uso.</summary>
	public const string EmailRefused = Prefix + "email-recusado";
}

/// <summary>Categoria do fato, para o operador separar a trilha de perfis da de contas (metadata <c>category</c>).</summary>
public static class DirectorySyncCategories
{
	/// <summary>Reconciliação de perfis (entrega A).</summary>
	public const string Roles = "roles";

	/// <summary>Provisionamento de contas (entrega B).</summary>
	public const string Accounts = "accounts";
}

/// <summary>Quem disparou a reconciliação.</summary>
public enum DirectorySyncTrigger
{
	/// <summary>Login federado da própria conta.</summary>
	Login,

	/// <summary>Ciclo periódico.</summary>
	Cycle,
}

/// <summary>
/// Um fato a registrar na trilha da sincronização (ADR-0055). Só valores do servidor: ids e nomes do próprio
/// banco, contagens e o código do erro traduzido — nunca texto do pedido, nunca detalhe de exceção.
/// </summary>
/// <param name="TenantId">Tenant em que o fato aconteceu.</param>
/// <param name="DirectoryId">Directory id da federação (identifica o ator <c>directory</c>).</param>
/// <param name="Action">Uma das constantes de <see cref="DirectorySyncActions"/>.</param>
/// <param name="Trigger">Login ou ciclo.</param>
/// <param name="UserId">Conta afetada, quando o fato é sobre uma conta; senão o recurso é o tenant.</param>
/// <param name="RoleName">Perfil acrescentado, retirado ou protegido.</param>
/// <param name="GroupId">Grupo do diretório que justifica o fato.</param>
/// <param name="ErrorCode">Código do erro traduzido do Graph, na falha de leitura.</param>
/// <param name="Count">Contagem relevante (retiradas pretendidas), no aborto.</param>
/// <param name="Total">Total de referência (atribuições <c>Directory</c> ou contas geridas do tenant), no aborto.</param>
/// <param name="Category">Categoria do fato (<see cref="DirectorySyncCategories"/>); <c>null</c> quando a ação basta.</param>
/// <param name="Reason">Motivo estável (código do servidor), na recusa de e-mail e na desativação barrada.</param>
/// <param name="ObjectId">Object id (<c>oid</c>) da pessoa no diretório, quando ainda não há conta — nunca o e-mail dela.</param>
public sealed record DirectorySyncAuditRecord(
	Guid TenantId,
	Guid DirectoryId,
	string Action,
	DirectorySyncTrigger Trigger,
	Guid? UserId = null,
	string? RoleName = null,
	Guid? GroupId = null,
	string? ErrorCode = null,
	int? Count = null,
	int? Total = null,
	string? Category = null,
	string? Reason = null,
	Guid? ObjectId = null);

/// <summary>
/// Trilha da sincronização com o diretório, guardada no SecureGate (ADR-0054/0055). <c>true</c> significa entrada
/// confirmada no banco; só com ele a mudança acontece (fail-closed).
/// </summary>
public interface IDirectorySyncAuditor
{
	/// <summary>Grava o fato, de forma síncrona.</summary>
	/// <param name="record">Fato a registrar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se o registro não pôde ser gravado.</returns>
	Task<bool> RecordAsync(DirectorySyncAuditRecord record, CancellationToken cancellationToken = default);
}
