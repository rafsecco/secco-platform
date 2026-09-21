using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Pedido de troca do próprio e-mail (entrega C), pelo cookie do login interativo.
/// </summary>
/// <remarks>
/// Exige a senha atual de quem tem senha local: sem isso, um cookie sequestrado apontaria a conta
/// para a caixa do atacante, e a recuperação de senha passaria a ser dele.
/// <para>
/// Depois do pedido a tela mostra <b>sempre a mesma mensagem</b>, endereço livre ou em uso
/// (ADR-0020). O custo assumido é que quem digita errado descobre pela ausência do e-mail.
/// </para>
/// </remarks>
/// <param name="handler">Caso de uso do pedido.</param>
/// <param name="tokens">Porta de credencial, para saber se a conta usa senha local.</param>
/// <param name="userManager">Gerenciador de usuários do Identity.</param>
// O esquema é o cookie do login interativo, não o JwtBearer padrão da API (ADR-0007).
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class ChangeEmailModel(
	RequestEmailChangeHandler handler,
	ICredentialTokens tokens,
	UserManager<User> userManager) : PageModel
{
	/// <summary>Campos do formulário.</summary>
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>E-mail atual, mostrado na tela.</summary>
	public string CurrentEmail { get; private set; } = string.Empty;

	/// <summary>Conta com senha local mostra o campo de senha; conta de diretório, não (ADR-0026).</summary>
	public bool RequiresPassword { get; private set; }

	/// <summary>Verdadeiro depois do pedido — a tela passa à confirmação genérica.</summary>
	public bool Submitted { get; private set; }

	/// <summary>Mensagem de erro do formulário.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Novo endereço e senha atual.</summary>
	public sealed class InputModel
	{
		/// <summary>Endereço pretendido.</summary>
		[Required(ErrorMessage = "Informe o novo e-mail.")]
		[EmailAddress(ErrorMessage = "E-mail inválido.")]
		[StringLength(256, ErrorMessage = "E-mail muito longo.")]
		public string NewEmail { get; set; } = string.Empty;

		/// <summary>Senha atual — exigida de quem tem senha local.</summary>
		// ADR-0020: teto contra amplificação de custo no hashing PBKDF2.
		[StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
		[DataType(DataType.Password)]
		public string? CurrentPassword { get; set; }
	}

	/// <summary>Renderiza o formulário.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
	{
		if (await LoadAsync(cancellationToken).ConfigureAwait(false) is null)
		{
			return NotFound();
		}

		return Page();
	}

	/// <summary>Dispara a confirmação, quando houver o que disparar.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		if (await LoadAsync(cancellationToken).ConfigureAwait(false) is not { } account)
		{
			return NotFound();
		}

		if (!ModelState.IsValid)
		{
			return Page();
		}

		var accepted = await handler.HandleAsync(
			account.UserId,
			Input.NewEmail,
			Input.CurrentPassword,
			HttpContext.Connection.RemoteIpAddress?.ToString(),
			cancellationToken).ConfigureAwait(false);

		if (!accepted)
		{
			ErrorMessage = "Senha atual incorreta.";

			return Page();
		}

		Submitted = true;

		return Page();
	}

	private async Task<CredentialAccount?> LoadAsync(CancellationToken cancellationToken)
	{
		if (userManager.GetUserId(User) is not { } id
			|| !Guid.TryParse(id, out var userId)
			|| await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false) is not { } account
			|| !account.CanReceiveCredentialMail)
		{
			return null;
		}

		CurrentEmail = account.Email;
		RequiresPassword = account.HasPassword;

		return account;
	}
}
