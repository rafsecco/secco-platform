using System.Text.Json;
using Secco.LogStream.Domain.Audit;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.Audit;

/// <summary>Comando de criação de uma entrada de auditoria.</summary>
/// <param name="ActorId">Identificador do ator: <c>sub</c> do usuário, ou client id. Obrigatório.</param>
/// <param name="ActorType">Natureza do ator.</param>
/// <param name="Action">Ação realizada, verbo canônico. Obrigatória.</param>
/// <param name="ActorName">Snapshot legível do ator no momento do fato, quando informado.</param>
/// <param name="ResourceType">Tipo do recurso afetado, quando informado.</param>
/// <param name="ResourceId">Identificador do recurso afetado, quando informado.</param>
/// <param name="Metadata">JSON livre com contexto adicional, quando informado.</param>
/// <param name="CorrelationId">Correlation id da requisição de origem (populado pela borda).</param>
/// <param name="OccurredAt">Momento em que o fato aconteceu, declarado pelo chamador; nulo usa o momento corrente.</param>
public sealed record CreateAuditEntryCommand(
	string? ActorId,
	ActorType ActorType,
	string? Action,
	string? ActorName = null,
	string? ResourceType = null,
	string? ResourceId = null,
	string? Metadata = null,
	Guid? CorrelationId = null,
	DateTimeOffset? OccurredAt = null);

/// <summary>
/// Valida (ADR-0020: formato e tamanho de todo input externo) e persiste — <b>ingestão
/// SÍNCRONA</b>, a diferença deliberada deste recurso frente aos outros três do LogStream.
/// Um registro que existe por obrigação legal não pode desaparecer numa fila cheia sem que
/// ninguém saiba: este handler grava e só então devolve sucesso, com o Id definitivo — ou o
/// fato está gravado, ou o chamador recebe erro explícito e decide o que fazer. NÃO trocar
/// por <c>ILogIngestionQueue</c>: a assincronia dos outros recursos foi descartada de
/// propósito aqui (ver design doc, seção "Trilha de auditoria — Ingestão síncrona").
/// </summary>
public sealed class CreateAuditEntryHandler(IAuditEntryRepository repository, LogStreamIngestionOptions options)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<AuditEntryDto>> HandleAsync(
		CreateAuditEntryCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var validation = Validate(command, options);

		if (validation.IsFailure)
		{
			return Result.Failure<AuditEntryDto>(validation.Error);
		}

		var auditEntry = new AuditEntry(
			command.ActorId!,
			command.ActorType,
			command.Action!,
			command.ActorName,
			command.ResourceType,
			command.ResourceId,
			command.Metadata,
			command.CorrelationId,
			command.OccurredAt);

		await repository.AddAsync(auditEntry, cancellationToken).ConfigureAwait(false);

		return AuditEntryDto.FromEntity(auditEntry);
	}

	private static Result Validate(CreateAuditEntryCommand command, LogStreamIngestionOptions options)
	{
		if (string.IsNullOrWhiteSpace(command.ActorId))
		{
			return Result.Failure(LogStreamErrors.AuditEntries.ActorIdRequired);
		}

		if (command.ActorId.Length > options.MaxAuditActorIdLength)
		{
			return Result.Failure(LogStreamErrors.AuditEntries.ActorIdTooLong(options.MaxAuditActorIdLength));
		}

		if (command.ActorName is not null && command.ActorName.Length > options.MaxAuditActorNameLength)
		{
			return Result.Failure(LogStreamErrors.AuditEntries.ActorNameTooLong(options.MaxAuditActorNameLength));
		}

		if (string.IsNullOrWhiteSpace(command.Action))
		{
			return Result.Failure(LogStreamErrors.AuditEntries.ActionRequired);
		}

		if (command.Action.Length > options.MaxAuditActionLength)
		{
			return Result.Failure(LogStreamErrors.AuditEntries.ActionTooLong(options.MaxAuditActionLength));
		}

		if (command.ResourceType is not null && command.ResourceType.Length > options.MaxAuditResourceTypeLength)
		{
			return Result.Failure(LogStreamErrors.AuditEntries.ResourceTypeTooLong(options.MaxAuditResourceTypeLength));
		}

		if (command.ResourceId is not null && command.ResourceId.Length > options.MaxAuditResourceIdLength)
		{
			return Result.Failure(LogStreamErrors.AuditEntries.ResourceIdTooLong(options.MaxAuditResourceIdLength));
		}

		if (command.Metadata is not null)
		{
			if (command.Metadata.Length > options.MaxAuditMetadataLength)
			{
				return Result.Failure(LogStreamErrors.AuditEntries.MetadataTooLong(options.MaxAuditMetadataLength));
			}

			if (!IsValidJson(command.Metadata))
			{
				return Result.Failure(LogStreamErrors.AuditEntries.MetadataInvalidJson);
			}
		}

		return Result.Success();
	}

	private static bool IsValidJson(string value)
	{
		try
		{
			using var _ = JsonDocument.Parse(value);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
