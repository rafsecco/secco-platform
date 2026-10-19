using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Directory;

/// <summary>Lista, paginadas, as pessoas do tenant do token (ADR-0045).</summary>
public sealed class ListDirectoryPeopleHandler(IDirectoryReader reader)
{
	/// <summary>Tamanho máximo de página do diretório (mais estrito que o teto do SharedKernel).</summary>
	public const int MaxPageSize = 100;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da claim do token — nunca de rota, query ou header.</param>
	/// <param name="page">Página pedida (1-based).</param>
	/// <param name="size">Tamanho pedido; acima de <see cref="MaxPageSize"/> é reduzido a ele.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<DirectoryPersonDto>>> HandleAsync(
		Guid tenantId, int? page, int? size, CancellationToken cancellationToken = default)
	{
		var request = new PageRequest(page ?? PageRequest.FirstPage, Math.Min(size ?? PageRequest.DefaultSize, MaxPageSize));
		var people = await reader.ListAsync(tenantId, request, cancellationToken).ConfigureAwait(false);

		return PagedResult.Create<DirectoryPersonDto>([.. people.Items.Select(DirectoryPersonDto.From)], request, people.TotalCount);
	}
}
