using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Permissions;
using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Permissions;

/// <summary>
/// Publica o catálogo de permissões de um produto (ADR-0039). Valida tudo ANTES de persistir
/// (ADR-0020: input externo): produto kebab-case, 0 a 200 entradas, nome no formato canônico ou
/// família <c>prefixo-{param}:acao</c> (ADR-0043), descrição de 1 a 200 caracteres após o trim e
/// sem caractere de controle (log forging), sem nome repetido. Colisão de nome com outro produto NUNCA bloqueia (o repositório a registra em log).
/// </summary>
public sealed class PublishPermissionCatalogHandler(IPermissionCatalogRepository repository)
{
	/// <summary>Máximo de entradas por produto.</summary>
	public const int MaxPermissions = 200;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(PublishPermissionCatalogCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var product = command.Product ?? string.Empty;

		// Sem normalização de caixa: o produto da rota é comparado ordinal com o escopo (ADR-0039)
		if (!PermissionCatalogProducts.IsValidName(product))
		{
			return Result.Failure(SecureGateErrors.PermissionCatalog.ProductInvalid);
		}

		if (command.Permissions is null)
		{
			return Result.Failure(SecureGateErrors.PermissionCatalog.PermissionsRequired);
		}

		if (command.Permissions.Count > MaxPermissions)
		{
			return Result.Failure(SecureGateErrors.PermissionCatalog.TooManyPermissions);
		}

		var definitions = new List<SeccoPermissionDefinition>(command.Permissions.Count);
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var input in command.Permissions)
		{
			if (input is null || !(SeccoPermissions.IsValid(input.Name) || SeccoPermissions.IsValidFamily(input.Name)))
			{
				return Result.Failure(SecureGateErrors.PermissionCatalog.PermissionNameInvalid);
			}

			var description = input.Description?.Trim();

			if (string.IsNullOrEmpty(description)
				|| description.Length > PermissionCatalogEntry.DescriptionMaxLength
				|| description.Any(char.IsControl))
			{
				return Result.Failure(SecureGateErrors.PermissionCatalog.DescriptionInvalid);
			}

			if (!seen.Add(input.Name!))
			{
				return Result.Failure(SecureGateErrors.PermissionCatalog.DuplicateName);
			}

			definitions.Add(new SeccoPermissionDefinition(input.Name!, description));
		}

		// Colisão de nome com outro produto é logada pelo repositório (Infrastructure) e não bloqueia
		await repository
			.ReplaceAsync(product, definitions, DateTimeOffset.UtcNow, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
