using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Cadastro do segundo fator (entrega D), pelo cookie do login interativo.
/// </summary>
/// <remarks>
/// O QR é gerado no servidor e embutido na página: um gerador externo receberia o segredo TOTP da
/// pessoa, o que anularia o segundo fator antes mesmo de ele existir (ADR-0020).
/// <para>
/// Os códigos de recuperação são renderizados <b>apenas na resposta que os gerou</b> — não passam
/// por <c>TempData</c> nem por sessão, e um F5 não os traz de volta. Quem não anotou, gera outros.
/// </para>
/// <para>
/// Conta que entra só pelo diretório responde 404, como a tela de trocar senha: ali não há senha a
/// proteger com segundo fator, porque o MFA é do diretório (ADR-0026).
/// </para>
/// </remarks>
/// <param name="setup">Porta de cadastro do segundo fator.</param>
/// <param name="enableHandler">Caso de uso de ligar.</param>
/// <param name="disableHandler">Caso de uso de desligar.</param>
/// <param name="tokens">Porta de credencial, para saber se a conta usa senha local.</param>
/// <param name="userManager">Gerenciador de usuários do Identity.</param>
/// <param name="signInManager">Gerenciador de login, para renovar o cookie após a mudança.</param>
// O esquema é o cookie do login interativo, não o JwtBearer padrão da API (ADR-0007).
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class TwoFactorModel(
	ITwoFactorSetup setup,
	EnableTwoFactorHandler enableHandler,
	DisableTwoFactorHandler disableHandler,
	ICredentialTokens tokens,
	UserManager<User> userManager,
	SignInManager<User> signInManager) : PageModel
{
	/// <summary>Campos do formulário de confirmação.</summary>
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>Segundo fator já ativado nesta conta.</summary>
	public bool Enabled { get; private set; }

	/// <summary>Códigos de recuperação restantes, quando o 2FA está ligado.</summary>
	public int RecoveryCodesLeft { get; private set; }

	/// <summary>Chave em blocos, para quem prefere digitar em vez de ler o QR.</summary>
	public string? FormattedKey { get; private set; }

	/// <summary>QR embutido (<c>data:</c> URI), quando há cadastro em andamento.</summary>
	public string? QrCodeDataUri { get; private set; }

	/// <summary>
	/// Códigos recém-gerados. Vivem só nesta resposta: recarregar a página não os traz de volta.
	/// </summary>
	public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

	/// <summary>Mensagem de erro do formulário.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Código digitado na confirmação.</summary>
	public sealed class InputModel
	{
		/// <summary>Código de seis dígitos exibido pelo aplicativo autenticador.</summary>
		[Required(ErrorMessage = "Informe o código do aplicativo.")]
		[StringLength(16, ErrorMessage = "Código muito longo.")]
		public string Code { get; set; } = string.Empty;
	}

	/// <summary>Mostra o estado atual e, quando não há 2FA, inicia um cadastro.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
	{
		if (await LoadAsync(cancellationToken).ConfigureAwait(false) is not { } userId)
		{
			return NotFound();
		}

		if (!Enabled)
		{
			await StartEnrollmentAsync(userId, cancellationToken).ConfigureAwait(false);
		}

		return Page();
	}

	/// <summary>Confirma o código e liga o segundo fator.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
	{
		if (await LoadAsync(cancellationToken).ConfigureAwait(false) is not { } userId)
		{
			return NotFound();
		}

		if (!ModelState.IsValid)
		{
			await StartEnrollmentAsync(userId, cancellationToken).ConfigureAwait(false);

			return Page();
		}

		var codes = await enableHandler.HandleAsync(userId, Input.Code, cancellationToken).ConfigureAwait(false);

		if (codes.Count == 0)
		{
			ErrorMessage = "Código inválido. Confira o horário do dispositivo e tente o código atual.";
			await StartEnrollmentAsync(userId, cancellationToken).ConfigureAwait(false);

			return Page();
		}

		// A revogação derrubou as sessões; renovar o cookie mantém a desta janela, como na troca
		// de senha — ninguém é deslogado pelo próprio ato.
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is { } user)
		{
			await signInManager.RefreshSignInAsync(user).ConfigureAwait(false);
		}

		Enabled = true;
		RecoveryCodes = codes;
		RecoveryCodesLeft = codes.Count;

		return Page();
	}

	/// <summary>Desliga o segundo fator e zera o cadastro.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostDisableAsync(CancellationToken cancellationToken)
	{
		if (await LoadAsync(cancellationToken).ConfigureAwait(false) is not { } userId)
		{
			return NotFound();
		}

		await disableHandler.HandleAsync(userId, cancellationToken).ConfigureAwait(false);

		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is { } user)
		{
			await signInManager.RefreshSignInAsync(user).ConfigureAwait(false);
		}

		return RedirectToPage("/Account/TwoFactor");
	}

	/// <summary>Carrega o estado; devolve <c>null</c> quando a conta não usa senha local.</summary>
	private async Task<Guid?> LoadAsync(CancellationToken cancellationToken)
	{
		if (userManager.GetUserId(User) is not { } id
			|| !Guid.TryParse(id, out var userId)
			|| await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false) is not { } account
			|| !account.LocalLoginEnabled
			|| !account.HasPassword)
		{
			return null;
		}

		if (await setup.GetStateAsync(userId, cancellationToken).ConfigureAwait(false) is { } state)
		{
			Enabled = state.Enabled;
			RecoveryCodesLeft = state.RecoveryCodesLeft;
		}

		return userId;
	}

	private async Task StartEnrollmentAsync(Guid userId, CancellationToken cancellationToken)
	{
		if (await setup.StartEnrollmentAsync(userId, cancellationToken).ConfigureAwait(false) is { } enrollment)
		{
			FormattedKey = enrollment.FormattedKey;
			QrCodeDataUri = enrollment.QrCodeDataUri;
		}
	}
}
