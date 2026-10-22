using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Domain.TenantAdministration;

namespace Secco.SecureGate.Infrastructure.TenantAdministration;

/// <summary>
/// Grava cada escrita da gestão do tenant por máquina no banco do SecureGate (ADR-0054), no escopo do tenant
/// administrado — nunca em modo plataforma. Síncrono e fail-closed: <c>true</c> significa entrada confirmada no
/// banco, e só com ele a operação roda. Não depende de <c>SecureGate:Audit</c> nem do LogStream.
/// </summary>
internal sealed partial class DatabaseTenantManageAuditor(
	IAuthorityAuditRepository repository,
	ITenantAccessScope accessScope,
	TimeProvider clock,
	ILogger<DatabaseTenantManageAuditor> logger) : ITenantManageAuditor
{
	/// <summary>Prefixo das ações desta trilha.</summary>
	public const string ActionPrefix = "gestao-do-tenant.";

	/// <summary>Tipo de ator da gestão por máquina.</summary>
	public const string ClientActorType = "client";

	public async Task<bool> RecordAsync(TenantManageAuditRecord record, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(record);

		// Sem ator identificado não há registro, e sem registro não há escrita
		if (string.IsNullOrEmpty(record.ClientId))
		{
			LogAuditFailed(logger, "?", record.Operation, "SemAtor");

			return false;
		}

		try
		{
			// Metadata montada aqui, só com valores do servidor (token, rota e nome da operação) — nunca do pedido
			var metadata = JsonSerializer.Serialize(new
			{
				authority = SecureGateScopes.TenantManage,
				act = record.ClientId,
				tenantId = record.TenantId,
			});

			var entry = new AuthorityAuditEntry(
				record.TenantId,
				ClientActorType,
				record.ClientId,
				actorName: null,
				ActionPrefix + record.Operation,
				record.TargetUserId is null ? "tenant" : "user",
				(record.TargetUserId ?? record.TenantId).ToString(),
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
#pragma warning disable CA1031 // Falha de qualquer natureza tem o mesmo desfecho: sem registro, sem escrita (fail-closed)
		catch (Exception exception)
#pragma warning restore CA1031
		{
			// Só o TIPO da exceção (ADR-0020): a mensagem pode trazer trecho de SQL ou de connection string
			LogAuditFailed(logger, record.ClientId, record.Operation, exception.GetType().Name);

			return false;
		}
	}

	[LoggerMessage(
		EventId = 3202,
		Level = LogLevel.Warning,
		Message = "Trilha de autoridade falhou para {Actor} em {Operation} ({ExceptionType}); a escrita foi recusada.")]
	private static partial void LogAuditFailed(ILogger logger, string actor, string operation, string exceptionType);
}
