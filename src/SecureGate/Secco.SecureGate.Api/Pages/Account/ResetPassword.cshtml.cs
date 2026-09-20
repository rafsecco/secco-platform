using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Tela que consome o link de redefinição (ADR-0033). Ao concluir, <b>todas as sessões da conta
/// caem</b> (ADR-0032): redefinir senha é a resposta a um comprometimento, e quem entrou com a
/// senha antiga não pode continuar dentro.
/// </summary>
/// <remarks>
/// Link recusado nunca diz o motivo — expirado, já usado, de outra conta ou conta desativada
/// levam à mesma tela (ADR-0020). Como na tela de convite, não existe endpoint de API equivalente.
/// </remarks>
/// <param name="handler">Caso de uso de redefinição.</param>
[AllowAnonymous]
public sealed class ResetPasswordModel(ResetPasswordHandler handler) : PageModel
{
	/// <summary>Usuário do link.</summary>
	[BindProperty(SupportsGet = true)]
	public Guid UserId { get; set; }

	/// <summary>Token do link.</summary>
	[BindProperty(SupportsGet = true)]
	public string Token { get; set; } = string.Empty;

	/// <summary>Campos do formulário.</summary>
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>Verdadeiro quando o link não vale mais.</summary>
	public bool LinkRejected { get; private set; }

	/// <summary>Mensagem de erro do formulário.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Senha e confirmação.</summary>
	public sealed class InputModel
	{
		/// <summary>Nova senha.</summary>
		[Required(ErrorMessage = "Informe a senha.")]
		// ADR-0020: teto contra amplificação de custo no hashing PBKDF2.
		[StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
		[DataType(DataType.Password)]
		public string Password { get; set; } = string.Empty;

		/// <summary>Confirmação.</summary>
		[Required(ErrorMessage = "Confirme a senha.")]
		[Compare(nameof(Password), ErrorMessage = "As senhas não conferem.")]
		[DataType(DataType.Password)]
		public string ConfirmPassword { get; set; } = string.Empty;
	}

	/// <summary>Renderiza o formulário, ou a tela de link recusado quando ele é malformado.</summary>
	public void OnGet()
	{
		if (UserId == Guid.Empty || string.IsNullOrWhiteSpace(Token))
		{
			LinkRejected = true;
		}
	}

	/// <summary>Consome o link, troca a senha e encerra as sessões.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
		{
			return Page();
		}

		var outcome = await handler
			.HandleAsync(UserId, Token, invite: false, Input.Password, cancellationToken)
			.ConfigureAwait(false);

		switch (outcome)
		{
			case CredentialTokenOutcome.Done:
				return RedirectToPage("/Login");

			case CredentialTokenOutcome.WeakPassword:
				ErrorMessage = "A senha não atende à política: use ao menos 8 caracteres, com maiúscula, minúscula, número e símbolo.";

				return Page();

			default:
				LinkRejected = true;

				return Page();
		}
	}
}
