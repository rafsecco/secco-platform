using System.Security.Claims;
using OpenIddict.Abstractions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SecureGate.Infrastructure.OpenIddict;
using Secco.SharedKernel.Constants;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Decide em que tenant o login acontece e se a pessoa pode receber token nele (ADR-0040). Um lugar só,
/// usado no authorize e na renovação/troca do code.
/// </summary>
/// <remarks>
/// O tenant do login NUNCA vem de parâmetro da requisição: vem do registro do client (authorize) ou do
/// token que o próprio OpenIddict validou (renovação). Os perfis do token são só os do tenant do login —
/// <c>UserManager.GetRolesAsync</c> devolve os de todos os tenants da conta e não pode ser usado aqui.
/// </remarks>
internal sealed class LoginTenantResolver(
	IOpenIddictApplicationManager applicationManager,
	ITenantRepository tenants,
	ITenantMembershipRepository memberships,
	IUserDirectory users,
	ITenantAccessScope accessScope)
{
	/// <summary>
	/// Tenant do login no authorize: o do client interativo vinculado (<c>OidcApplication.TenantId</c>),
	/// ou o tenant dono da conta quando o client não é vinculado (plataforma).
	/// </summary>
	/// <param name="clientId">Client da requisição de autorização.</param>
	/// <param name="user">Conta autenticada pelo cookie.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Guid> ForAuthorizeAsync(string? clientId, User user, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(user);

		if (!string.IsNullOrEmpty(clientId)
			&& await applicationManager.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false)
				is OidcApplication { TenantId: { } tenantId })
		{
			return tenantId;
		}

		return user.TenantId;
	}

	/// <summary>
	/// Tenant da renovação/troca do code: a claim <c>tenant_id</c> do principal validado pelo OpenIddict;
	/// sem a claim (operador, ADR-0024), o tenant dono.
	/// </summary>
	/// <param name="stored">Principal do code/refresh que o OpenIddict já validou.</param>
	/// <param name="user">Conta recarregada do cadastro.</param>
	public static Guid ForRenewal(ClaimsPrincipal stored, User user)
	{
		ArgumentNullException.ThrowIfNull(stored);
		ArgumentNullException.ThrowIfNull(user);

		return Guid.TryParse(stored.GetClaim(SeccoClaims.TenantId), out var tenantId) ? tenantId : user.TenantId;
	}

	/// <summary>
	/// Se a pessoa pode receber token no tenant do login: é o dono (pela coluna, nunca pela linha de
	/// vínculo) ou tem vínculo ativo E o tenant do login está ativo. O estado da conta e do tenant dono é
	/// do <see cref="AccountStateGuard"/>.
	/// </summary>
	/// <param name="user">Conta.</param>
	/// <param name="loginTenantId">Tenant do login.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<bool> CanIssueAsync(User user, Guid loginTenantId, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(user);

		if (loginTenantId == user.TenantId)
		{
			return true;
		}

		// O tenant do login é conhecido (do client ou do token emitido pelo próprio SecureGate): o vínculo é lido nele
		using (accessScope.EnterTenant(loginTenantId))
		{
			if (!await memberships.IsActiveMemberAsync(loginTenantId, user.Id, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}
		}

		return await tenants.GetByIdAsync(loginTenantId, cancellationToken).ConfigureAwait(false) is { IsActive: true };
	}

	/// <summary>Perfis da pessoa só no tenant do login.</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="loginTenantId">Tenant do login.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, Guid loginTenantId, CancellationToken cancellationToken)
	{
		// A pessoa foi achada em modo plataforma, mas os perfis do token são SÓ os do tenant do login
		// (ADR-0040): o filtro dos perfis passa a ser garantido pelo escopo, não só pela consulta (ADR-0041).
		using (accessScope.EnterTenant(loginTenantId))
		{
			return await users.GetRolesInTenantAsync(userId, loginTenantId, cancellationToken).ConfigureAwait(false);
		}
	}
}
