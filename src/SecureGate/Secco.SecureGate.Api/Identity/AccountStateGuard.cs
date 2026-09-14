using Microsoft.AspNetCore.Identity;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Decide se uma conta ainda pode receber token, a partir do estado ATUAL do cadastro — não do
/// token apresentado, que segue criptograficamente válido depois de a conta ser bloqueada.
/// </summary>
/// <remarks>
/// Um lugar só, usado pela renovação de token, pela troca do authorization code e pela elevação
/// (ADR-0031). Existia antes em forma incompleta nos dois primeiros e completa só no terceiro — e
/// foi exatamente essa divergência que deixou a renovação ignorar bloqueio e tenant desativado.
/// <para>
/// <c>SignInManager.CanSignInAsync</c> sozinho NÃO basta: ele checa confirmação de conta, não
/// bloqueio nem tenant. Como a desativação de usuário é lockout, sem a segunda checagem desativar
/// não teria efeito nenhum sobre uma sessão já aberta.
/// </para>
/// </remarks>
internal static class AccountStateGuard
{
	/// <summary>Indica se a conta pode receber um token agora.</summary>
	/// <param name="user">Conta, recarregada do cadastro.</param>
	/// <param name="userManager">Gerenciador do Identity.</param>
	/// <param name="signInManager">Gerenciador de sign-in do Identity.</param>
	/// <param name="tenantRepository">Repositório de tenants.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<bool> CanReceiveTokensAsync(
		User user,
		UserManager<User> userManager,
		SignInManager<User> signInManager,
		ITenantRepository tenantRepository,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(user);

		if (!await signInManager.CanSignInAsync(user).ConfigureAwait(false))
		{
			return false;
		}

		// Bloqueio por tentativas E desativação pelo admin (que é lockout)
		if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
		{
			return false;
		}

		var tenant = await tenantRepository.GetByIdAsync(user.TenantId, cancellationToken).ConfigureAwait(false);

		return tenant is { IsActive: true };
	}
}
