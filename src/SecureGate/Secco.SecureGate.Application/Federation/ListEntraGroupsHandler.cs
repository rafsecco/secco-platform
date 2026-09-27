using Secco.SecureGate.Application.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Lista os grupos do diretório federado de um tenant (issue #27, ADR-0036). Só leitura — decidir
/// o que vira perfil continua sendo um ato do admin, na #28.
/// </summary>
public sealed class ListEntraGroupsHandler(ITenantRepository tenants, IEntraGroupDirectory directory)
{
	/// <summary>Tamanho de página aplicado quando nenhum (ou um inválido) é informado.</summary>
	public const int DefaultPageSize = 20;

	/// <summary>Tamanho máximo de página; valores maiores são reduzidos a este teto.</summary>
	public const int MaxPageSize = 200;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="nameFilter">Termo de busca por nome, cru (ainda não sanitizado).</param>
	/// <param name="pageToken">Token opaco da página anterior; <c>null</c> para a primeira.</param>
	/// <param name="pageSize">Itens por página pedidos; normalizado silenciosamente.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<EntraGroupPage>> HandleAsync(
		Guid tenantId,
		string? nameFilter,
		string? pageToken,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		var federation = await tenants.GetFederationAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (federation is not { IsEnabled: true })
		{
			return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.NotEnabled);
		}

		string? sanitizedFilter;

		try
		{
			sanitizedFilter = EntraGroupNameFilter.Sanitize(nameFilter);
		}
		catch (ArgumentException)
		{
			return Result.Failure<EntraGroupPage>(SecureGateErrors.Federation.SearchTermInvalid);
		}

		var normalizedSize = pageSize switch
		{
			< 1 => DefaultPageSize,
			> MaxPageSize => MaxPageSize,
			_ => pageSize,
		};

		return await directory
			.ListGroupsAsync(federation.DirectoryId, sanitizedFilter, pageToken, normalizedSize, cancellationToken)
			.ConfigureAwait(false);
	}
}
