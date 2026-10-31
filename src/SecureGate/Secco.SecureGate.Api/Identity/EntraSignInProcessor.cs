using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Decisão fail-closed do login federado (ADR-0026): recebe o principal autenticado pelo
/// Entra ID (cookie externo) e decide se ele corresponde a um usuário PRÉ-PROVISIONADO
/// habilitado a entrar — o diretório do cliente prova identidade, mas NUNCA decide quem
/// tem acesso. Toda recusa devolve o MESMO erro genérico (não revela existência de conta,
/// ADR-0020); o motivo real vai apenas ao log do servidor, sem dados sensíveis.
/// </summary>
public sealed class EntraSignInProcessor(
	UserManager<User> userManager,
	ITenantRepository tenantRepository,
	DirectoryRoleReconciler reconciler,
	ILogger<EntraSignInProcessor> logger)
{
	/// <summary>Nome do provider persistido em <c>tb_user_logins</c>.</summary>
	public const string LoginProvider = DirectoryLinkKey.Provider;

	private const string DisplayName = "Microsoft Entra ID";

	/// <summary>
	/// Processa o principal externo: casa por vínculo (<c>{tid}:{oid}</c>) ou, no primeiro
	/// login, por e-mail dentro do diretório registrado do tenant do usuário; re-verifica
	/// federação/diretório/tenant/bloqueio a cada login.
	/// </summary>
	/// <param name="externalPrincipal">Principal autenticado pelo esquema externo do Entra ID.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O usuário habilitado a entrar, ou o erro genérico de federação.</returns>
	public async Task<Result<User>> ProcessAsync(
		ClaimsPrincipal externalPrincipal,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(externalPrincipal);

		// Claims crus do Entra (MapInboundClaims desligado, ADR-0007): tid, oid, email
		if (!Guid.TryParse(externalPrincipal.FindFirstValue("tid"), out var directoryId)
			|| !Guid.TryParse(externalPrincipal.FindFirstValue("oid"), out var objectId))
		{
			EntraSignInProcessorLog.MissingTokenClaims(logger);
			return Result.Failure<User>(SecureGateErrors.Federation.SignInRejected);
		}

		var providerKey = DirectoryLinkKey.Build(directoryId, objectId);
		var user = await userManager.FindByLoginAsync(LoginProvider, providerKey).ConfigureAwait(false);
		var firstFederatedLogin = user is null;

		if (user is null)
		{
			// Primeiro login federado: casa por e-mail — o pin de diretório abaixo garante que
			// o casamento só vale dentro do tid registrado do tenant DO USUÁRIO (ADR-0026)
			var email = externalPrincipal.FindFirstValue("email");

			if (string.IsNullOrWhiteSpace(email))
			{
				EntraSignInProcessorLog.MissingEmailClaim(logger, directoryId);
				return Result.Failure<User>(SecureGateErrors.Federation.SignInRejected);
			}

			user = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

			if (user is null)
			{
				// Usuário não provisionado — fail-closed, sem revelar a inexistência (ADR-0020)
				EntraSignInProcessorLog.UserNotProvisioned(logger, directoryId, objectId);
				return Result.Failure<User>(SecureGateErrors.Federation.SignInRejected);
			}
		}

		var federation = await tenantRepository.GetFederationAsync(user.TenantId, cancellationToken).ConfigureAwait(false);

		if (federation is null || !federation.IsEnabled || federation.DirectoryId != directoryId)
		{
			EntraSignInProcessorLog.FederationRejected(logger, user.TenantId, directoryId);
			return Result.Failure<User>(SecureGateErrors.Federation.SignInRejected);
		}

		var tenant = await tenantRepository.GetByIdAsync(user.TenantId, cancellationToken).ConfigureAwait(false);

		if (tenant is null || !tenant.IsActive)
		{
			EntraSignInProcessorLog.TenantInactive(logger, user.TenantId);
			return Result.Failure<User>(SecureGateErrors.Federation.SignInRejected);
		}

		if (await userManager.IsLockedOutAsync(user).ConfigureAwait(false))
		{
			EntraSignInProcessorLog.UserLockedOut(logger, user.TenantId, objectId);
			return Result.Failure<User>(SecureGateErrors.Federation.SignInRejected);
		}

		if (firstFederatedLogin)
		{
			// Vincula o oid (imutável no diretório) — logins seguintes não dependem mais do e-mail
			var link = await userManager
				.AddLoginAsync(user, new UserLoginInfo(LoginProvider, providerKey, DisplayName))
				.ConfigureAwait(false);

			if (!link.Succeeded)
			{
				EntraSignInProcessorLog.LinkFailed(logger, user.TenantId, objectId);
				return Result.Failure<User>(SecureGateErrors.Federation.SignInRejected);
			}
		}

		await ReconcileRolesAsync(user, directoryId, objectId, cancellationToken).ConfigureAwait(false);

		return Result.Success(user);
	}

	/// <summary>
	/// Reconciliação de perfis da conta com os grupos mapeados do diretório (ADR-0055). Roda DEPOIS de a identidade
	/// estar provada e a conta liberada, e nunca derruba o login: falha do Graph (ou da própria reconciliação) não
	/// muda nada e o login segue com os perfis que a conta já tinha. Sem mapeamento no tenant, nenhuma chamada.
	/// </summary>
	private async Task ReconcileRolesAsync(User user, Guid directoryId, Guid objectId, CancellationToken cancellationToken)
	{
		try
		{
			var result = await reconciler
				.ReconcileLoginAsync(user.TenantId, directoryId, user.Id, objectId, cancellationToken)
				.ConfigureAwait(false);

			if (result.Outcome is DirectorySyncOutcome.ReadFailed or DirectorySyncOutcome.AbortedByBrake)
			{
				EntraSignInProcessorLog.ReconciliationSkipped(logger, user.TenantId, result.Outcome, result.ErrorCode ?? "-");
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
#pragma warning disable CA1031 // A reconciliação é melhor esforço no login: o login nunca depende dela
		catch (Exception exception)
#pragma warning restore CA1031
		{
			// Só o TIPO da exceção (ADR-0020): a mensagem pode trazer trecho de SQL ou de resposta do Graph
			EntraSignInProcessorLog.ReconciliationFailed(logger, user.TenantId, exception.GetType().Name);
		}
	}
}

/// <summary>Mensagens de log do <see cref="EntraSignInProcessor"/> (CA1848). Nunca logam e-mail ou token.</summary>
internal static partial class EntraSignInProcessorLog
{
	[LoggerMessage(
		EventId = 1,
		Level = LogLevel.Warning,
		Message = "Login federado recusado: token do Entra sem tid/oid válidos.")]
	public static partial void MissingTokenClaims(ILogger logger);

	[LoggerMessage(
		EventId = 2,
		Level = LogLevel.Warning,
		Message = "Login federado recusado: token do Entra sem claim email (tid {DirectoryId}).")]
	public static partial void MissingEmailClaim(ILogger logger, Guid directoryId);

	[LoggerMessage(
		EventId = 3,
		Level = LogLevel.Warning,
		Message = "Login federado recusado: e-mail não provisionado (tid {DirectoryId}, oid {ObjectId}).")]
	public static partial void UserNotProvisioned(ILogger logger, Guid directoryId, Guid objectId);

	[LoggerMessage(
		EventId = 4,
		Level = LogLevel.Warning,
		Message = "Login federado recusado: federação ausente, desabilitada ou diretório divergente (tenant {TenantId}, tid {DirectoryId}).")]
	public static partial void FederationRejected(ILogger logger, Guid tenantId, Guid directoryId);

	[LoggerMessage(
		EventId = 5,
		Level = LogLevel.Warning,
		Message = "Login federado recusado: tenant inativo ou inexistente ({TenantId}).")]
	public static partial void TenantInactive(ILogger logger, Guid tenantId);

	[LoggerMessage(
		EventId = 6,
		Level = LogLevel.Warning,
		Message = "Login federado recusado: conta bloqueada (tenant {TenantId}, oid {ObjectId}).")]
	public static partial void UserLockedOut(ILogger logger, Guid tenantId, Guid objectId);

	[LoggerMessage(
		EventId = 7,
		Level = LogLevel.Warning,
		Message = "Login federado recusado: falha ao vincular login externo (tenant {TenantId}, oid {ObjectId}).")]
	public static partial void LinkFailed(ILogger logger, Guid tenantId, Guid objectId);

	[LoggerMessage(
		EventId = 8,
		Level = LogLevel.Warning,
		Message = "Login federado seguiu sem reconciliar perfis (tenant {TenantId}): {Outcome}, {ErrorCode}.")]
	public static partial void ReconciliationSkipped(ILogger logger, Guid tenantId, DirectorySyncOutcome outcome, string errorCode);

	[LoggerMessage(
		EventId = 9,
		Level = LogLevel.Warning,
		Message = "Login federado seguiu sem reconciliar perfis (tenant {TenantId}): falha {ExceptionType}.")]
	public static partial void ReconciliationFailed(ILogger logger, Guid tenantId, string exceptionType);
}
