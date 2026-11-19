using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Federation;
using Secco.SecureGate.Infrastructure.OpenIddict;

namespace Secco.SecureGate.Api.Identity;

/// <summary>Para onde o authorize manda a pessoa sem sessão: o diretório do tenant e a dica de domínio.</summary>
/// <param name="TenantId">Tenant dono do client.</param>
/// <param name="DirectoryId">Diretório (tenant do Entra) registrado na federação.</param>
/// <param name="DomainHint">Primeiro domínio de e-mail permitido da federação, ou <c>null</c>.</param>
internal sealed record DirectSignInTarget(Guid TenantId, Guid DirectoryId, string? DomainHint);

/// <summary>
/// Decide se o <c>/connect/authorize</c> sem sessão vai direto ao Entra ID (login federado direto). A decisão
/// sai só do registro do client e da federação do tenant dele — nunca de parâmetro da requisição.
/// </summary>
internal sealed class DirectSignInPolicy(
	IOpenIddictApplicationManager applications,
	ITenantRepository tenants,
	ITenantAccessScope accessScope,
	IOptions<SecureGateEntraIdOptions> entra)
{
	/// <summary>
	/// Alvo do login direto, ou <c>null</c> quando o fluxo segue pela tela local: Entra não configurado, client
	/// inexistente ou sem tenant, tenant sem federação, federação desabilitada ou sem a opção ligada.
	/// </summary>
	/// <param name="clientId">Client da requisição de autorização.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<DirectSignInTarget?> ResolveAsync(string? clientId, CancellationToken cancellationToken)
	{
		if (!entra.Value.IsConfigured || string.IsNullOrEmpty(clientId))
		{
			return null;
		}

		if (await applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false)
			is not OidcApplication { TenantId: { } tenantId })
		{
			return null;
		}

		using (accessScope.EnterTenant(tenantId))
		{
			var federation = await tenants.GetFederationAsync(tenantId, cancellationToken).ConfigureAwait(false);

			if (federation is not { IsEnabled: true, DirectSignIn: true })
			{
				return null;
			}

			var domains = federation.GetAllowedEmailDomains();

			return new DirectSignInTarget(tenantId, federation.DirectoryId, domains.Count > 0 ? domains[0] : null);
		}
	}
}
