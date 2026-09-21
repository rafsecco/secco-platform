using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Confirma a troca de e-mail pelo link enviado ao endereço novo (entrega C).
/// </summary>
/// <remarks>
/// Anônima de propósito: quem confirma costuma abrir o link na caixa nova, que pode estar em
/// outro navegador. Quem autoriza é o token, não a sessão — e o token só vale para o endereço
/// que o gerou.
/// <para>
/// A confirmação acontece no próprio GET: aqui o link <b>é</b> a ação. Isso difere das telas de
/// senha, onde o GET só valida e o POST executa, porque lá existe um formulário a preencher.
/// </para>
/// </remarks>
/// <param name="handler">Caso de uso da confirmação.</param>
[AllowAnonymous]
public sealed class ConfirmEmailModel(ConfirmEmailChangeHandler handler) : PageModel
{
	/// <summary>Usuário do link.</summary>
	[BindProperty(SupportsGet = true)]
	public Guid UserId { get; set; }

	/// <summary>Endereço novo, o mesmo que o token carrega.</summary>
	[BindProperty(SupportsGet = true, Name = "email")]
	public string NewEmail { get; set; } = string.Empty;

	/// <summary>Token do link.</summary>
	[BindProperty(SupportsGet = true)]
	public string Token { get; set; } = string.Empty;

	/// <summary>Verdadeiro quando a troca foi efetivada.</summary>
	public bool Confirmed { get; private set; }

	/// <summary>Consome o link.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task OnGetAsync(CancellationToken cancellationToken)
	{
		if (UserId == Guid.Empty || string.IsNullOrWhiteSpace(NewEmail) || string.IsNullOrWhiteSpace(Token))
		{
			return;
		}

		// Link expirado, já usado, de outro endereço ou de endereço tomado no meio do caminho
		// levam todos à mesma tela (ADR-0020).
		Confirmed = await handler.HandleAsync(UserId, NewEmail, Token, cancellationToken).ConfigureAwait(false)
			== CredentialTokenOutcome.Done;
	}
}
