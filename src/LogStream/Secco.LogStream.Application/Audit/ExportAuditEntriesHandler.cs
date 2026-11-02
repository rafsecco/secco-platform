using System.Runtime.CompilerServices;
using System.Text.Json;
using Secco.LogStream.Domain.Audit;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.Audit;

/// <summary>Quem exportou, como o token o identifica.</summary>
/// <param name="Id"><c>sub</c> do token.</param>
/// <param name="Type">Pessoa ou máquina.</param>
/// <param name="Name">Nome de exibição, quando a pessoa tem.</param>
public sealed record AuditExportActor(string Id, ActorType Type, string? Name);

/// <summary>
/// Exportação da trilha de auditoria (ADR-0058). Prepara as linhas com os filtros e o tenant da
/// busca, e registra a própria exportação na trilha. O registro é síncrono e fail-closed: a borda
/// só envia o arquivo depois dele, e se ele falhar o arquivo não sai.
/// </summary>
public sealed class ExportAuditEntriesHandler(IAuditEntryRepository repository, CreateAuditEntryHandler createAuditEntry)
{
	/// <summary>Ação gravada na trilha.</summary>
	public const string ExportAction = "audit-entries.export";

	/// <summary>Valores de filtro entram na trilha com tamanho limitado: podem trazer dado pessoal.</summary>
	private const int MaxFilterValueLength = 200;

	/// <summary>Valida os filtros e devolve as linhas, lidas uma a uma do banco do tenant atual.</summary>
	/// <param name="criteria">Filtros, os mesmos da busca.</param>
	public Result<IAsyncEnumerable<AuditEntryDto>> Prepare(AuditEntrySearchCriteria criteria)
	{
		ArgumentNullException.ThrowIfNull(criteria);

		if (criteria.From is not null && criteria.To is not null && criteria.From > criteria.To)
		{
			return LogStreamErrors.AuditEntries.InvalidDateRange;
		}

		return Result<IAsyncEnumerable<AuditEntryDto>>.Success(Project(repository.StreamAsync(criteria)));
	}

	/// <summary>Registra quem exportou, com que filtros e quantas linhas saíram. Nunca o conteúdo.</summary>
	/// <param name="actor">Quem exportou.</param>
	/// <param name="format">Formato do arquivo.</param>
	/// <param name="rowCount">Linhas que saíram.</param>
	/// <param name="criteria">Filtros aplicados.</param>
	/// <param name="correlationId">Correlation id da requisição.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> RecordAsync(
		AuditExportActor actor,
		string format,
		int rowCount,
		AuditEntrySearchCriteria criteria,
		Guid? correlationId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(actor);
		ArgumentNullException.ThrowIfNull(criteria);

		var filters = new Dictionary<string, string>(StringComparer.Ordinal);
		Add(filters, "from", criteria.From?.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
		Add(filters, "to", criteria.To?.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
		Add(filters, "actorId", criteria.ActorId);
		Add(filters, "action", criteria.Action);
		Add(filters, "resourceType", criteria.ResourceType);
		Add(filters, "resourceId", criteria.ResourceId);
		Add(filters, "correlationId", criteria.CorrelationId?.ToString());

		var metadata = JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["format"] = format,
			["rows"] = rowCount,
			["filters"] = filters,
		});

		var recorded = await createAuditEntry.HandleAsync(
			new CreateAuditEntryCommand(
				actor.Id,
				actor.Type,
				ExportAction,
				actor.Name,
				ResourceType: "audit-entries",
				Metadata: metadata,
				CorrelationId: correlationId),
			cancellationToken).ConfigureAwait(false);

		return recorded.IsSuccess ? Result.Success() : Result.Failure(recorded.Error);
	}

	private static void Add(Dictionary<string, string> filters, string name, string? value)
	{
		if (value is not null)
		{
			filters[name] = value.Length <= MaxFilterValueLength ? value : value[..MaxFilterValueLength];
		}
	}

	private static async IAsyncEnumerable<AuditEntryDto> Project(
		IAsyncEnumerable<AuditEntry> entries,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
		{
			yield return AuditEntryDto.FromEntity(entry);
		}
	}
}
