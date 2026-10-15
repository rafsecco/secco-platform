using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Validação comum a criação e alteração (ADR-0037, ADR-0020): formato antes do banco, e papéis
/// resolvidos para o nome canônico de <c>tb_roles</c> — o token carrega exatamente o que o
/// resolvedor de permissões vai procurar.
/// </summary>
public sealed class ProductClientAccessValidator(IRoleRepository roles, IRedirectUriPolicy? redirectPolicy = null)
{
	/// <summary>Valida o comando e devolve o acesso normalizado.</summary>
	public async Task<Result<ProductClientAccess>> ValidateAsync(
		Guid tenantId, ProductClientCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var name = command.Name?.Trim() ?? string.Empty;

		if (!ProductClientRules.IsValidName(name))
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.NameInvalid);
		}

		var type = command.Type ?? ProductClientRules.ClientCredentialsType;

		if (type is not (ProductClientRules.ClientCredentialsType or ProductClientRules.AuthorizationCodeType))
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.TypeInvalid);
		}

		var interactive = type == ProductClientRules.AuthorizationCodeType;
		IReadOnlyList<string>? redirectUris = null;
		IReadOnlyList<string>? postLogoutRedirectUris = null;

		if (interactive)
		{
			if (command.Roles is { Count: > 0 })
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RolesNotAllowedForInteractive);
			}

			var allowInsecure = redirectPolicy?.AllowInsecure ?? false;
			redirectUris = [.. command.RedirectUris ?? []];
			postLogoutRedirectUris = [.. command.PostLogoutRedirectUris ?? []];

			if (redirectUris.Count is 0 or > ProductClientRules.MaxRedirectUris || !AreValid(redirectUris, allowInsecure))
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RedirectUrisInvalid);
			}

			if (postLogoutRedirectUris.Count > ProductClientRules.MaxRedirectUris || !AreValid(postLogoutRedirectUris, allowInsecure))
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.PostLogoutRedirectUrisInvalid);
			}
		}
		else if (command.RedirectUris is { Count: > 0 } || command.PostLogoutRedirectUris is { Count: > 0 })
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RedirectUrisNotAllowed);
		}

		var scopes = command.Scopes ?? [];

		if (scopes.Count is 0 or > ProductClientRules.MaxScopes || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Count)
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.ScopesInvalid);
		}

		if (scopes.Any(scope => !SecureGateScopes.IsProductScope(scope)))
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.ScopeNotAllowed);
		}

		var requestedRoles = (command.Roles ?? []).Select(role => role?.Trim() ?? string.Empty).ToList();

		if (requestedRoles.Count > ProductClientRules.MaxRoles
			|| requestedRoles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requestedRoles.Count)
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RolesInvalid);
		}

		var canonicalRoles = new List<string>(requestedRoles.Count);

		foreach (var role in requestedRoles)
		{
			if (!RoleInputRules.IsValidName(role))
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RoleNotFound);
			}

			// Antes do banco: a lista de reservados é pública. Inclui o de operador — "assinável a
			// usuários" não vale para máquina: client de produto nunca é operador de instalação.
			if (RoleInputRules.IsReservedName(role))
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RoleNotAssignable);
			}

			if (await roles.FindRoleAsync(tenantId, role, cancellationToken).ConfigureAwait(false) is not { } found)
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RoleNotFound);
			}

			canonicalRoles.Add(found.Name);
		}

		return Result.Success(new ProductClientAccess(
			name, [.. scopes], canonicalRoles, type, redirectUris, postLogoutRedirectUris));
	}

	private static bool AreValid(IReadOnlyList<string> uris, bool allowInsecure) =>
		uris.All(uri => ProductClientRules.IsValidRedirectUri(uri, allowInsecure))
		&& uris.Distinct(StringComparer.Ordinal).Count() == uris.Count;
}
