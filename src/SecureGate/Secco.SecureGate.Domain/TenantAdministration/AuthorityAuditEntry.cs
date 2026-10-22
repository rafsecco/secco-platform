using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.TenantAdministration;

/// <summary>
/// Entrada da trilha de autoridade guardada no próprio SecureGate (ADR-0054): hoje, uma escrita da gestão do
/// próprio tenant por máquina (<c>securegate:tenant-manage</c>, ADR-0052). Dado de tenant — o do tenant
/// administrado —, isolado pelo filtro <c>"Tenant"</c> e pelo Row-Level Security (ADR-0041). Só se acrescenta:
/// não há operação de alteração nem de exclusão, a não ser a retenção opt-in. O instante é sempre o do
/// SERVIDOR, nunca um valor declarado por quem chama.
/// </summary>
public sealed class AuthorityAuditEntry : BaseEntity
{
	/// <summary>Tamanho máximo do tipo de ator (coluna <c>ds_actor_type</c>).</summary>
	public const int ActorTypeMaxLength = 32;

	/// <summary>Tamanho máximo do identificador do ator (coluna <c>ds_actor_id</c>).</summary>
	public const int ActorIdMaxLength = 200;

	/// <summary>Tamanho máximo do nome do ator (coluna <c>ds_actor_name</c>).</summary>
	public const int ActorNameMaxLength = 200;

	/// <summary>Tamanho máximo da ação (coluna <c>ds_action</c>).</summary>
	public const int ActionMaxLength = 200;

	/// <summary>Tamanho máximo do tipo do recurso (coluna <c>ds_resource_type</c>).</summary>
	public const int ResourceTypeMaxLength = 100;

	/// <summary>Tamanho máximo do id do recurso (coluna <c>ds_resource_id</c>).</summary>
	public const int ResourceIdMaxLength = 200;

	/// <summary>Tamanho máximo da <c>Metadata</c> em JSON (coluna <c>ds_metadata</c>).</summary>
	public const int MetadataMaxLength = 4000;

	private AuthorityAuditEntry()
	{
		// Construtor de rehidratação do EF Core
		ActorType = string.Empty;
		ActorId = string.Empty;
		Action = string.Empty;
		ResourceType = string.Empty;
		ResourceId = string.Empty;
	}

	/// <summary>Registra a entrada.</summary>
	/// <param name="tenantId">Tenant administrado (onde a entrada é isolada). Obrigatório.</param>
	/// <param name="actorType">Tipo do ator (<c>client</c> na gestão por máquina). Obrigatório.</param>
	/// <param name="actorId">Identificador do ator (o client id). Obrigatório.</param>
	/// <param name="actorName">Nome legível do ator, se houver.</param>
	/// <param name="action">Ação (<c>gestao-do-tenant.&lt;operationId&gt;</c>). Obrigatória.</param>
	/// <param name="resourceType">Tipo do recurso afetado. Obrigatório.</param>
	/// <param name="resourceId">Identificador do recurso afetado. Obrigatório.</param>
	/// <param name="metadata">JSON montado pelo servidor — nunca texto vindo do pedido.</param>
	/// <param name="now">Instante do servidor (por parâmetro, para ser testável).</param>
	/// <exception cref="DomainInvariantException">Se algum argumento violar os invariantes.</exception>
	public AuthorityAuditEntry(
		Guid tenantId,
		string actorType,
		string actorId,
		string? actorName,
		string action,
		string resourceType,
		string resourceId,
		string? metadata,
		DateTimeOffset now)
	{
		if (tenantId == Guid.Empty)
		{
			throw new DomainInvariantException("Uma entrada da trilha de autoridade exige o tenant.");
		}

		TenantId = tenantId;
		ActorType = Required(actorType, ActorTypeMaxLength, "o tipo do ator");
		ActorId = Required(actorId, ActorIdMaxLength, "o identificador do ator");
		ActorName = Optional(actorName, ActorNameMaxLength, "o nome do ator");
		Action = Required(action, ActionMaxLength, "a ação");
		ResourceType = Required(resourceType, ResourceTypeMaxLength, "o tipo do recurso");
		ResourceId = Required(resourceId, ResourceIdMaxLength, "o id do recurso");
		Metadata = Optional(metadata, MetadataMaxLength, "a metadata");
		CreatedAt = now;
	}

	/// <summary>Tenant administrado (coluna <c>id_fk_tenant</c>) — o que o filtro e o RLS isolam.</summary>
	public Guid TenantId { get; private set; }

	/// <summary>Tipo do ator (coluna <c>ds_actor_type</c>).</summary>
	public string ActorType { get; private set; }

	/// <summary>Identificador do ator (coluna <c>ds_actor_id</c>).</summary>
	public string ActorId { get; private set; }

	/// <summary>Nome legível do ator (coluna <c>ds_actor_name</c>).</summary>
	public string? ActorName { get; private set; }

	/// <summary>Ação registrada (coluna <c>ds_action</c>).</summary>
	public string Action { get; private set; }

	/// <summary>Tipo do recurso afetado (coluna <c>ds_resource_type</c>).</summary>
	public string ResourceType { get; private set; }

	/// <summary>Identificador do recurso afetado (coluna <c>ds_resource_id</c>).</summary>
	public string ResourceId { get; private set; }

	/// <summary>Metadata em JSON, montada pelo servidor (coluna <c>ds_metadata</c>).</summary>
	public string? Metadata { get; private set; }

	/// <summary>Instante do servidor (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	private static string Required(string? value, int maxLength, string what)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
		{
			throw new DomainInvariantException($"Uma entrada da trilha de autoridade exige {what}, com até {maxLength} caracteres.");
		}

		return value;
	}

	private static string? Optional(string? value, int maxLength, string what)
	{
		if (value is null)
		{
			return null;
		}

		if (value.Length > maxLength)
		{
			throw new DomainInvariantException($"Uma entrada da trilha de autoridade tem {what} acima de {maxLength} caracteres.");
		}

		return value;
	}
}
