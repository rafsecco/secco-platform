using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secco.LogStream.Client;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Elevation;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Infrastructure.Elevation;

/// <summary>
/// Grava a auditoria de cada troca de elevação como <c>AuditEntry</c> no LogStream, no tenant de
/// quem elevou (ADR-0031 e emenda de 2026-09-13).
/// </summary>
/// <remarks>
/// A chamada usa o token da IDENTIDADE DE AUDITORIA (papel <c>installation-auditor</c>, única
/// escrita cross-tenant da plataforma), e nunca o token do usuário. A ingestão de auditoria do
/// LogStream é síncrona (issue #2), então um <c>true</c> aqui significa registro aceito — e é só
/// com ele que o token é emitido.
/// </remarks>
internal sealed partial class LogStreamElevationAuditor(
	IHttpClientFactory httpClientFactory,
	ILogger<LogStreamElevationAuditor> logger) : IElevationAuditor
{
	/// <summary>Nome do <c>HttpClient</c> com o client credentials da identidade de auditoria.</summary>
	public const string HttpClientName = "secco-securegate-elevation-audit";

	/// <summary>Ação registrada na trilha de auditoria.</summary>
	public const string AuditAction = "elevacao.token-emitido";

	/// <inheritdoc />
	public bool IsConfigured => true;

	/// <inheritdoc />
	public async Task<bool> RecordAsync(ElevationAuditRecord record, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(record);

		try
		{
			var http = httpClientFactory.CreateClient(HttpClientName);

			// O tenant de QUEM ELEVOU, vindo do cadastro: é ali que os admins daquela empresa veem
			// quem, entre os seus, atravessou a fronteira de tenant.
			http.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, record.TenantId.ToString());

			await new LogStreamClient(http).CreateAuditEntryAsync(
				new CreateAuditEntryRequest
				{
					ActorId = record.UserId.ToString(),
					ActorType = ActorType.User,
					ActorName = record.UserName,
					Action = AuditAction,
					ResourceType = "token",
					ResourceId = SecureGatePlatform.ElevationCapability,
					Metadata = JsonSerializer.Serialize(new
					{
						capability = SecureGatePlatform.ElevationCapability,
						clientId = record.ClientId,
						scopes = record.Scopes,
						expiresAt = record.ExpiresAt,
					}),
				},
				cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Requisição abortada pelo chamador: não há o que recusar, só propagar
			throw;
		}
#pragma warning disable CA1031 // Falha de qualquer natureza tem o mesmo desfecho: sem registro, sem troca (fail-closed)
		catch (Exception exception)
#pragma warning restore CA1031
		{
			// Só o TIPO da exceção (ADR-0020): a mensagem pode trazer trecho de resposta ou URL
			LogAuditFailed(logger, record.UserId, exception.GetType().Name);

			return false;
		}
	}

	[LoggerMessage(
		EventId = 3101,
		Level = LogLevel.Warning,
		Message = "Auditoria de elevação falhou para o usuário {UserId} ({ExceptionType}); a troca foi recusada.")]
	private static partial void LogAuditFailed(ILogger logger, Guid userId, string exceptionType);
}
