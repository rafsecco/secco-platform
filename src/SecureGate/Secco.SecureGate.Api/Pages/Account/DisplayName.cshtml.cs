using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Edição do próprio nome de exibição (#30), pelo cookie do login interativo.
/// </summary>
/// <remarks>
/// Ao contrário de senha e e-mail, não exige a senha atual nem encerra outras sessões: nome de
/// exibição é rótulo, não credencial — não abre nem fecha acesso. Ainda assim entra na trilha
/// (dentro do handler), porque é a única defesa contra alguém trocando o nome de outra pessoa
/// sem deixar rastro.
/// </remarks>
/// <param name="handler">Caso de uso da alteração.</param>
/// <param name="userManager">Gerenciador de usuários do Identity.</param>
// O esquema é o cookie do login interativo, não o JwtBearer padrão da API (ADR-0007).
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class DisplayNameModel(SetDisplayNameHandler handler, UserManager<User> userManager) : PageModel
{
	/// <summary>Campos do formulário.</summary>
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>Mensagem de erro do formulário.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Nome de exibição.</summary>
	public sealed class InputModel
	{
		/// <summary>Valor cru; em branco limpa o nome.</summary>
		[System.ComponentModel.DataAnnotations.StringLength(
			DisplayNameRules.MaxLength, ErrorMessage = "Máximo de 160 caracteres.")]
		public string? DisplayName { get; set; }
	}

	/// <summary>Carrega o valor atual.</summary>
	public async Task<IActionResult> OnGetAsync()
	{
		if (await userManager.GetUserAsync(User).ConfigureAwait(false) is not { } user)
		{
			return NotFound();
		}

		Input.DisplayName = user.DisplayName;

		return Page();
	}

	/// <summary>Grava o novo nome.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		if (await userManager.GetUserAsync(User).ConfigureAwait(false) is not { } user)
		{
			return NotFound();
		}

		if (!ModelState.IsValid)
		{
			return Page();
		}

		var result = await handler
			.HandleAsync(new SetDisplayNameCommand(user.TenantId, user.Id, Input.DisplayName), cancellationToken)
			.ConfigureAwait(false);

		if (result.IsFailure)
		{
			ErrorMessage = "Nome inválido. Evite caracteres de controle e o limite de 160 caracteres.";

			return Page();
		}

		return RedirectToPage("/Account/Index", new { nomeAlterado = true });
	}
}
