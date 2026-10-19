using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secco.LogStream.Client;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Infrastructure.Elevation;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Infrastructure.TenantAdministration;

/// <summary>
/// Grava cada escrita por admin de tenant como <c>AuditEntry</c> no LogStream, no tenant que o registro indica
/// — o administrado, ou o dono de quem agiu quando o administrado não tem LogStream (ADR-0044/0047) —, pela
/// identidade de auditoria da instalação — a mesma da elevação (ADR-0031). A ingestão
/// de auditoria do LogStream é síncrona, então um <c>true</c> aqui significa registro aceito; só com ele
/// a escrita acontece.
/// </summary>
internal sealed partial class LogStreamTenantAdminAuditor(
	IHttpClientFactory httpClientFactory,
	ILogger<LogStreamTenantAdminAuditor> logger) : ITenantAdminAuditor
{
	/// <summary>Prefixo das ações desta trilha.</summary>
	public const string ActionPrefix = "admin-de-tenant.";

	/// <inheritdoc />
	public bool IsConfigured => true;

	/// <inheritdoc />
	public async Task<bool> RecordAsync(TenantAdminAuditRecord record, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(record);

		try
		{
			var http = httpClientFactory.CreateClient(LogStreamElevationAuditor.HttpClientName);

			// O destino já vem decidido no registro (ADR-0047); o administrado segue na Metadata
			http.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, record.TenantId.ToString());

			await new LogStreamClient(http).CreateAuditEntryAsync(
				new CreateAuditEntryRequest
				{
					ActorId = record.UserId.ToString(),
					ActorType = ActorType.User,
					ActorName = record.UserName,
					Action = ActionPrefix + record.Operation,
					ResourceType = record.TargetUserId is null ? "tenant" : "user",
					ResourceId = (record.TargetUserId ?? record.TenantId).ToString(),
					Metadata = JsonSerializer.Serialize(new
					{
						authority = SecureGateScopes.TenantAdmin,
						act = record.ClientId,
						tenantId = record.AdministeredTenantId,
					}),
				},
				cancellationToken).ConfigureAwait(false);

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
			// Só o TIPO da exceção (ADR-0020): a mensagem pode trazer trecho de resposta ou URL
			LogAuditFailed(logger, record.UserId, record.Operation, exception.GetType().Name);

			return false;
		}
	}

	[LoggerMessage(
		EventId = 3201,
		Level = LogLevel.Warning,
		Message = "Auditoria de admin de tenant falhou para {UserId} em {Operation} ({ExceptionType}); a escrita foi recusada.")]
	private static partial void LogAuditFailed(ILogger logger, Guid userId, string operation, string exceptionType);
}

/// <summary>
/// Auditor de uma instalação SEM identidade de auditoria (ADR-0044): não audita e, por isso, desliga a
/// autoridade de admin de tenant inteira.
/// </summary>
internal sealed class NotConfiguredTenantAdminAuditor : ITenantAdminAuditor
{
	/// <summary>Instância única — não há estado.</summary>
	public static readonly NotConfiguredTenantAdminAuditor Instance = new();

	private NotConfiguredTenantAdminAuditor()
	{
	}

	/// <inheritdoc />
	public bool IsConfigured => false;

	/// <inheritdoc />
	public Task<bool> RecordAsync(TenantAdminAuditRecord record, CancellationToken cancellationToken = default) =>
		Task.FromResult(false);
}
