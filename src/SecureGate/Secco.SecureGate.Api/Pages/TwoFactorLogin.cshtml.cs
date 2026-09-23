using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Pages;

/// <summary>
/// Segundo passo do login (entrega D): dígito do autenticador ou código de recuperação.
/// </summary>
/// <remarks>
/// Anônima por natureza — quem chega aqui ainda não tem sessão. O que identifica a pessoa é o
/// <b>cookie de duas etapas</b> do Identity, gravado pelo passo da senha; ele não autentica nada
/// sozinho, só diz "esta pessoa passou pela senha e falta o segundo fator".
/// <para>
/// Tentativa errada alimenta o lockout: força bruta no dígito de seis casas é o ataque óbvio
/// contra TOTP (ADR-0020).
/// </para>
/// </remarks>
/// <param name="signInManager">Gerenciador de login.</param>
/// <param name="setup">Estado do segundo fator, para avisar quantos códigos restam.</param>
/// <param name="auditor">Trilha (best-effort).</param>
[AllowAnonymous]
public sealed class TwoFactorLoginModel(
	SignInManager<User> signInManager,
	ITwoFactorSetup setup,
	ICredentialAuditor auditor) : PageModel
{
	/// <summary>Campos do formulário.</summary>
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>Destino após o login (a requisição de autorização original). Local apenas.</summary>
	[BindProperty(SupportsGet = true)]
	public string? ReturnUrl { get; set; }

	/// <summary>Mensagem de erro exibida após uma tentativa malsucedida.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Códigos de recuperação restantes, quando já são poucos.</summary>
	public int? RecoveryCodesLeft { get; private set; }

	/// <summary>Código digitado.</summary>
	public sealed class InputModel
	{
		/// <summary>Dígito do autenticador ou código de recuperação.</summary>
		[Required(ErrorMessage = "Informe o código.")]
		[StringLength(64, ErrorMessage = "Código muito longo.")]
		public string Code { get; set; } = string.Empty;

		/// <summary>Indica que o valor digitado é um código de recuperação, não o dígito.</summary>
		public bool IsRecoveryCode { get; set; }
	}

	/// <summary>Renderiza o formulário, se houver um primeiro passo concluído.</summary>
	public async Task<IActionResult> OnGetAsync() =>
		await signInManager.GetTwoFactorAuthenticationUserAsync().ConfigureAwait(false) is null
			? RedirectToPage("/Login")
			: Page();

	/// <summary>Confere o código e conclui o login.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		var user = await signInManager.GetTwoFactorAuthenticationUserAsync().ConfigureAwait(false);

		if (user is null)
		{
			return RedirectToPage("/Login");
		}

		if (!ModelState.IsValid)
		{
			return Page();
		}

		var code = Input.Code.Replace(" ", string.Empty, StringComparison.Ordinal);

		// lockoutOnFailure é padrão nos dois métodos: tentativa errada conta para o bloqueio.
		var result = Input.IsRecoveryCode
			? await signInManager.TwoFactorRecoveryCodeSignInAsync(code).ConfigureAwait(false)
			: await signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false)
				.ConfigureAwait(false);

		if (!result.Succeeded)
		{
			ErrorMessage = result.IsLockedOut
				? "Conta temporariamente bloqueada por excesso de tentativas. Tente novamente em alguns minutos."
				: "Código inválido.";

			return Page();
		}

		if (Input.IsRecoveryCode)
		{
			// Um código a menos: a trilha registra, e a tela seguinte avisa quantos sobraram.
			await auditor.RecordAsync(
				CredentialAuditEvent.TwoFactorRecoveryCodeUsed, user.Id, user.TenantId, user.Email, cancellationToken)
				.ConfigureAwait(false);

			RecoveryCodesLeft = (await setup.GetStateAsync(user.Id, cancellationToken).ConfigureAwait(false))
				?.RecoveryCodesLeft;
		}

		// LocalRedirect: recusa URLs absolutas — sem open redirect (ADR-0020).
		return LocalRedirect(ReturnUrl ?? "/conta");
	}
}
