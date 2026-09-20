using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Troca da própria senha (ADR-0033), pelo cookie do login interativo.
/// </summary>
/// <remarks>
/// Exige a senha atual — é o que impede uma sessão sequestrada de trocar a credencial e expulsar
/// o dono. Depois da troca, a sessão desta janela é <b>renovada</b> e as demais caem (ADR-0032):
/// quem está trocando a senha não deveria ser deslogado pelo próprio ato.
/// <para>
/// Conta que entra só pelo diretório corporativo (ADR-0026) não tem senha a trocar, e a página
/// responde 404 — a mesma resposta de rota inexistente, sem contar nada sobre a conta.
/// </para>
/// </remarks>
/// <param name="handler">Caso de uso da troca.</param>
/// <param name="tokens">Porta de tokens, para saber se a conta usa senha local.</param>
/// <param name="userManager">Gerenciador de usuários do Identity.</param>
/// <param name="signInManager">Gerenciador de login, para renovar o cookie.</param>
// O esquema é o cookie do login interativo, não o JwtBearer padrão da API (ADR-0007):
// IdentityConstants.ApplicationScheme não é constante de compilação, então vai literal.
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ChangePasswordModel(
	ChangeOwnPasswordHandler handler,
	ICredentialTokens tokens,
	UserManager<User> userManager,
	SignInManager<User> signInManager) : PageModel
{
	/// <summary>Campos do formulário.</summary>
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>Mensagem de erro do formulário.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Senha atual, nova e confirmação.</summary>
	public sealed class InputModel
	{
		/// <summary>Senha atual.</summary>
		[Required(ErrorMessage = "Informe a senha atual.")]
		[StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
		[DataType(DataType.Password)]
		public string CurrentPassword { get; set; } = string.Empty;

		/// <summary>Nova senha.</summary>
		[Required(ErrorMessage = "Informe a nova senha.")]
		// ADR-0020: teto contra amplificação de custo no hashing PBKDF2.
		[StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
		[DataType(DataType.Password)]
		public string Password { get; set; } = string.Empty;

		/// <summary>Confirmação da nova senha.</summary>
		[Required(ErrorMessage = "Confirme a nova senha.")]
		[Compare(nameof(Password), ErrorMessage = "As senhas não conferem.")]
		[DataType(DataType.Password)]
		public string ConfirmPassword { get; set; } = string.Empty;
	}

	/// <summary>Renderiza o formulário para quem tem senha local.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
		await UsesLocalPasswordAsync(cancellationToken).ConfigureAwait(false) ? Page() : NotFound();

	/// <summary>Troca a senha, encerra as outras sessões e renova a desta janela.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		var user = await userManager.GetUserAsync(User).ConfigureAwait(false);

		if (user is null || !await UsesLocalPasswordAsync(cancellationToken).ConfigureAwait(false))
		{
			return NotFound();
		}

		if (!ModelState.IsValid)
		{
			return Page();
		}

		var outcome = await handler
			.HandleAsync(user.Id, Input.CurrentPassword, Input.Password, cancellationToken)
			.ConfigureAwait(false);

		switch (outcome)
		{
			case CredentialTokenOutcome.Done:
				// Depois da revogação: o cookie antigo carrega o stamp antigo e seria recusado
				// pelo /connect/authorize na próxima navegação.
				await signInManager.RefreshSignInAsync(user).ConfigureAwait(false);

				return RedirectToPage("/Account/Index", new { senhaAlterada = true });

			case CredentialTokenOutcome.WeakPassword:
				ErrorMessage = "A senha não atende à política: use ao menos 8 caracteres, com maiúscula, minúscula, número e símbolo.";

				return Page();

			case CredentialTokenOutcome.InvalidToken:
				ErrorMessage = "Senha atual incorreta.";

				return Page();

			default:
				return NotFound();
		}
	}

	private async Task<bool> UsesLocalPasswordAsync(CancellationToken cancellationToken)
	{
		if (userManager.GetUserId(User) is not { } id || !Guid.TryParse(id, out var userId))
		{
			return false;
		}

		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		return account is { LocalLoginEnabled: true, HasPassword: true };
	}
}
