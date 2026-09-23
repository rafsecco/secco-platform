using Microsoft.Extensions.Logging;
using Secco.LogStream.Client;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Elevation;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Grava os eventos de credencial como <c>AuditEntry</c> no LogStream, no tenant do próprio
/// usuário, pela identidade de auditoria da instalação (ADR-0031/ADR-0033).
/// </summary>
/// <remarks>
/// Reusa o <c>HttpClient</c> nomeado da identidade de auditoria — a mesma que a elevação usa —
/// porque é a única credencial de máquina que o SecureGate já custodia para escrever no LogStream.
/// A diferença de postura é deliberada: aqui a falha é engolida com log (best-effort), enquanto na
/// elevação ela recusa a troca.
/// <para>
/// Nada do conteúdo sensível entra no registro: nem token, nem senha, nem o link. O que fica é
/// quem, quando e qual evento — o suficiente para reconstruir um incidente sem virar, ele próprio,
/// uma fonte de vazamento (ADR-0020).
/// </para>
/// </remarks>
internal sealed partial class LogStreamCredentialAuditor(
	IHttpClientFactory httpClientFactory,
	ILogger<LogStreamCredentialAuditor> logger) : ICredentialAuditor
{
	/// <summary>Prefixo das ações desta trilha.</summary>
	private const string ActionPrefix = "credencial.";

	/// <inheritdoc />
	public async Task RecordAsync(
		CredentialAuditEvent auditEvent,
		Guid userId,
		Guid tenantId,
		string? userName,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var http = httpClientFactory.CreateClient(LogStreamElevationAuditor.HttpClientName);
			http.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, tenantId.ToString());

			await new LogStreamClient(http).CreateAuditEntryAsync(
				new CreateAuditEntryRequest
				{
					ActorId = userId.ToString(),
					ActorType = ActorType.User,
					ActorName = userName,
					Action = ActionPrefix + ToSlug(auditEvent),
					ResourceType = "credencial",
					ResourceId = userId.ToString(),
				},
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
#pragma warning disable CA1031 // Best-effort (ADR-0033): nenhuma falha de trilha desfaz a operação
		catch (Exception exception)
#pragma warning restore CA1031
		{
			// Só o TIPO da exceção (ADR-0020): a mensagem pode trazer trecho de resposta ou URL
			LogAuditFailed(logger, auditEvent.ToString(), userId, exception.GetType().Name);
		}
	}

	/// <summary>Ação estável e legível na trilha, independente do nome do enum em C#.</summary>
	private static string ToSlug(CredentialAuditEvent auditEvent) => auditEvent switch
	{
		CredentialAuditEvent.PasswordSet => "senha-definida",
		CredentialAuditEvent.PasswordReset => "senha-redefinida",
		CredentialAuditEvent.InviteSent => "convite-enviado",
		CredentialAuditEvent.RecoveryRequested => "recuperacao-solicitada",
		CredentialAuditEvent.LinkRejected => "link-recusado",
		CredentialAuditEvent.LocalLoginDisabled => "login-local-desligado",
		CredentialAuditEvent.LocalLoginEnabled => "login-local-religado",
		CredentialAuditEvent.EmailChangeRequested => "email-troca-solicitada",
		CredentialAuditEvent.EmailChanged => "email-trocado",
		CredentialAuditEvent.ExternalLoginRemoved => "vinculo-externo-removido",
		CredentialAuditEvent.TwoFactorEnabled => "2fa-ligado",
		CredentialAuditEvent.TwoFactorDisabled => "2fa-desligado",
		CredentialAuditEvent.TwoFactorReset => "2fa-resetado",
		CredentialAuditEvent.TwoFactorRecoveryCodeUsed => "2fa-codigo-recuperacao-usado",
		_ => "desconhecido",
	};

	[LoggerMessage(
		EventId = 3301,
		Level = LogLevel.Warning,
		Message = "Auditoria de credencial ({Event}) falhou para o usuário {UserId} ({ExceptionType}); a operação seguiu.")]
	private static partial void LogAuditFailed(ILogger logger, string @event, Guid userId, string exceptionType);
}
