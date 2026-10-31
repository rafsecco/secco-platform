using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Domain.TenantAdministration;

namespace Secco.SecureGate.Infrastructure.TenantAdministration;

/// <summary>
/// Grava cada fato da sincronização com o diretório na trilha de autoridade do SecureGate (ADR-0054/0055), no
/// escopo do tenant afetado, com ator <c>directory</c> (identificado pelo directory id). Síncrono e fail-closed:
/// <c>true</c> significa entrada confirmada no banco, e só com ele a mudança acontece. A Metadata é montada aqui,
/// só com valores do servidor — nunca texto do pedido, nunca detalhe de exceção.
/// </summary>
internal sealed partial class DatabaseDirectorySyncAuditor(
	IAuthorityAuditRepository repository,
	ITenantAccessScope accessScope,
	TimeProvider clock,
	ILogger<DatabaseDirectorySyncAuditor> logger) : IDirectorySyncAuditor
{
	public async Task<bool> RecordAsync(DirectorySyncAuditRecord record, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(record);

		try
		{
			var metadata = JsonSerializer.Serialize(new
			{
				trigger = record.Trigger == DirectorySyncTrigger.Login ? "login" : "cycle",
				directoryId = record.DirectoryId,
				role = record.RoleName,
				groupId = record.GroupId,
				error = record.ErrorCode,
				count = record.Count,
				total = record.Total,
				category = record.Category,
				reason = record.Reason,
				objectId = record.ObjectId,
			});

			var entry = new AuthorityAuditEntry(
				record.TenantId,
				DirectorySyncActions.ActorType,
				record.DirectoryId.ToString("D"),
				actorName: null,
				record.Action,
				record.UserId is null ? "tenant" : "user",
				(record.UserId ?? record.TenantId).ToString("D"),
				metadata,
				clock.GetUtcNow());

			using (accessScope.EnterTenant(record.TenantId))
			{
				await repository.AddAsync(entry, cancellationToken).ConfigureAwait(false);
			}

			return true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
#pragma warning disable CA1031 // Falha de qualquer natureza tem o mesmo desfecho: sem registro, sem mudança (fail-closed)
		catch (Exception exception)
#pragma warning restore CA1031
		{
			// Só o TIPO da exceção (ADR-0020): a mensagem pode trazer trecho de SQL ou de connection string
			LogAuditFailed(logger, record.TenantId, record.Action, exception.GetType().Name);

			return false;
		}
	}

	[LoggerMessage(
		EventId = 3206,
		Level = LogLevel.Warning,
		Message = "Trilha da sincronização do diretório falhou (tenant {TenantId}, {Action}, {ExceptionType}); a mudança foi recusada.")]
	private static partial void LogAuditFailed(ILogger logger, Guid tenantId, string action, string exceptionType);
}
