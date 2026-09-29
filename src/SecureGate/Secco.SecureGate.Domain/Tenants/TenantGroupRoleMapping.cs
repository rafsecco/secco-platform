using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.Tenants;

/// <summary>
/// Mapeamento explícito de um grupo do diretório federado para um perfil do tenant (issue #28,
/// ADR-0036). Um grupo mapeia para exatamente um perfil na v1 — o admin do SecureGate é quem cria
/// este vínculo; o Entra nunca decide sozinho, só informa quem está no grupo.
/// </summary>
/// <remarks>
/// <see cref="EntraGroupDisplayName"/> é um SNAPSHOT legível do nome no momento do cadastro, não a
/// chave de casamento — o grupo pode ser renomeado no diretório do cliente sem que o mapeamento se
/// perca, porque o casamento real é por <see cref="EntraGroupId"/> (imutável).
/// </remarks>
public sealed class TenantGroupRoleMapping : BaseEntity
{
	/// <summary>Tamanho máximo aceito para o nome de exibição do grupo.</summary>
	public const int DisplayNameMaxLength = 256;

	private TenantGroupRoleMapping()
	{
		// Construtor de rehidratação do EF Core
		EntraGroupDisplayName = string.Empty;
	}

	/// <summary>Cria o mapeamento.</summary>
	/// <param name="tenantId">Tenant dono do mapeamento. Obrigatório.</param>
	/// <param name="entraGroupId">Id do grupo no Entra ID (imutável). Obrigatório.</param>
	/// <param name="entraGroupDisplayName">Nome do grupo no momento do cadastro (snapshot). Obrigatório.</param>
	/// <param name="roleId">Perfil do tenant que o grupo passa a conceder. Obrigatório.</param>
	/// <exception cref="DomainInvariantException">Se algum campo obrigatório estiver vazio ou o nome exceder o limite.</exception>
	public TenantGroupRoleMapping(Guid tenantId, Guid entraGroupId, string entraGroupDisplayName, Guid roleId)
	{
		if (tenantId == Guid.Empty)
		{
			throw new DomainInvariantException("Um mapeamento de grupo exige o tenant dono.");
		}

		if (entraGroupId == Guid.Empty)
		{
			throw new DomainInvariantException("Um mapeamento de grupo exige o id do grupo no Entra ID.");
		}

		if (string.IsNullOrWhiteSpace(entraGroupDisplayName))
		{
			throw new DomainInvariantException("Um mapeamento de grupo exige o nome de exibição do grupo.");
		}

		if (entraGroupDisplayName.Length > DisplayNameMaxLength)
		{
			throw new DomainInvariantException(
				$"O nome de exibição do grupo excede o limite de {DisplayNameMaxLength} caracteres.");
		}

		if (roleId == Guid.Empty)
		{
			throw new DomainInvariantException("Um mapeamento de grupo exige o perfil concedido.");
		}

		TenantId = tenantId;
		EntraGroupId = entraGroupId;
		EntraGroupDisplayName = entraGroupDisplayName.Trim();
		RoleId = roleId;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Tenant dono do mapeamento (coluna <c>id_fk_tenant</c>).</summary>
	public Guid TenantId { get; private set; }

	/// <summary>
	/// Id do grupo no Entra ID (coluna <c>entra_group_id</c>, sem prefixo — é um identificador
	/// externo, não uma FK local, mesmo padrão do <c>DirectoryId</c> da <see cref="TenantFederation"/>).
	/// Casamento real do vínculo — imutável mesmo que o grupo seja renomeado.
	/// </summary>
	public Guid EntraGroupId { get; private set; }

	/// <summary>Nome de exibição do grupo, snapshot do cadastro (coluna <c>ds_entra_group_display_name</c>).</summary>
	public string EntraGroupDisplayName { get; private set; }

	/// <summary>Perfil concedido por este mapeamento (coluna <c>id_fk_role</c>).</summary>
	public Guid RoleId { get; private set; }

	/// <summary>Momento do cadastro (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }
}
