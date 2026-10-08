using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Validação comum a criação e alteração (ADR-0037, ADR-0020): formato antes do banco, e papéis
/// resolvidos para o nome canônico de <c>tb_roles</c> — o token carrega exatamente o que o
/// resolvedor de permissões vai procurar.
/// </summary>
public sealed class ProductClientAccessValidator(IRoleRepository roles)
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

		return Result.Success(new ProductClientAccess(name, [.. scopes], canonicalRoles));
	}
}
