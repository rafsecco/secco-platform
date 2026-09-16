using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>Gestão de roles e permissões de um tenant, on-behalf-of o operador (Fase 7.2, ADR-0021).</summary>
public interface IRoleAdminService
{
	/// <summary>Tamanho fixo da página de membros na tela.</summary>
	const int MembersPageSize = 20;

	/// <summary>Lista os roles do tenant com suas permissões.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<RoleSummary>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Cria um role no tenant (sem permissões).</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="name">Nome do role.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task CreateRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

	/// <summary>Substitui o conjunto completo de permissões de um role (PUT idempotente).</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="role">Nome do role.</param>
	/// <param name="permissions">Conjunto completo desejado de permissões <c>recurso:acao</c>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SetPermissionsAsync(
		Guid tenantId, string role, IReadOnlyList<string> permissions,
		CancellationToken cancellationToken = default);

	/// <summary>Obtém o detalhe de um role: permissões, reserva de plataforma e quantidade de membros.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="role">Nome do role.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<RoleDetail> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default);

	/// <summary>Exclui um role vazio (nunca reservado).</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="role">Nome do role.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default);

	/// <summary>Lista os membros de um role, paginado em tamanho fixo (<see cref="MembersPageSize"/>).</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="role">Nome do role.</param>
	/// <param name="page">Página desejada (1-based).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<MemberPage> ListMembersAsync(Guid tenantId, string role, int page, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class SecureGateRoleAdminService(ISecureGateClientFactory clientFactory) : IRoleAdminService
{
	public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var roles = await client.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return [.. roles.Select(role => new RoleSummary(role.Name, [.. role.Permissions]))];
	}

	public async Task CreateRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.CreateRoleAsync(tenantId, new CreateRoleRequest { Name = name }, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task SetPermissionsAsync(
		Guid tenantId, string role, IReadOnlyList<string> permissions,
		CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.SetRolePermissionsAsync(
			tenantId, role, new SetRolePermissionsRequest { Permissions = [.. permissions] }, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<RoleDetail> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var detail = await client.GetRoleAsync(tenantId, role, cancellationToken).ConfigureAwait(false);

		return new RoleDetail(detail.Name, [.. detail.Permissions], detail.IsReserved, detail.MemberCount);
	}

	public async Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.DeleteRoleAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
	}

	public async Task<MemberPage> ListMembersAsync(Guid tenantId, string role, int page, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var result = await client
			.ListRoleMembersAsync(tenantId, role, page, IRoleAdminService.MembersPageSize, cancellationToken)
			.ConfigureAwait(false);

		return new MemberPage(
			[.. result.Items.Select(member => new RoleMemberSummary(member.UserId, member.Email, member.Status))],
			result.Page,
			result.TotalPages,
			result.TotalCount);
	}
}
