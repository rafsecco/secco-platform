using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Página da conta: o destino de quem entra pelo login sem vir de uma requisição de autorização.
/// </summary>
/// <remarks>
/// Existe porque o ciclo de credencial (ADR-0033) passou a levar a pessoa ao login <b>direto</b> —
/// antes dele, a tela só era alcançada pelo <c>/connect/authorize</c>, que sempre traz um destino.
/// Sem esta página, quem definia a senha e entrava caía na raiz do servidor de identidade, que não
/// serve nada.
/// <para>
/// É deliberadamente mínima: o SecureGate é servidor de identidade, não portal. Só o que a pessoa
/// pode fazer com a própria conta.
/// </para>
/// </remarks>
/// <param name="tokens">Porta de credencial, para saber se a conta usa senha local.</param>
/// <param name="userManager">Gerenciador de usuários do Identity.</param>
/// <param name="signInManager">Gerenciador de login, para o logout do cookie.</param>
// O esquema é o cookie do login interativo, não o JwtBearer padrão da API (ADR-0007).
[Authorize(AuthenticationSchemes = "Identity.Application")]
public sealed class IndexModel(
	ICredentialTokens tokens,
	ITwoFactorSetup twoFactorSetup,
	UserManager<User> userManager,
	SignInManager<User> signInManager) : PageModel
{
	/// <summary>E-mail da conta autenticada.</summary>
	public string Email { get; private set; } = string.Empty;

	/// <summary>Conta usa senha local (ADR-0026: desligado significa só diretório).</summary>
	public bool UsesLocalPassword { get; private set; }

	/// <summary>Segundo fator ativado (entrega D).</summary>
	public bool TwoFactorEnabled { get; private set; }

	/// <summary>Vem da troca de senha, para a confirmação aparecer aqui.</summary>
	[BindProperty(SupportsGet = true, Name = "senhaAlterada")]
	public bool PasswordChanged { get; set; }

	/// <summary>Carrega os dados da conta.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
	{
		if (userManager.GetUserId(User) is not { } id
			|| !Guid.TryParse(id, out var userId)
			|| await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false) is not { } account)
		{
			// Cookie de uma conta que não existe mais: sair é a única coisa sensata.
			await signInManager.SignOutAsync().ConfigureAwait(false);

			return RedirectToPage("/Login");
		}

		Email = account.Email;
		UsesLocalPassword = account is { LocalLoginEnabled: true, HasPassword: true };
		TwoFactorEnabled =
			(await twoFactorSetup.GetStateAsync(userId, cancellationToken).ConfigureAwait(false))?.Enabled ?? false;

		return Page();
	}

	/// <summary>Encerra a sessão do cookie e volta ao login.</summary>
	public async Task<IActionResult> OnPostLogoutAsync()
	{
		await signInManager.SignOutAsync().ConfigureAwait(false);

		return RedirectToPage("/Login");
	}
}
