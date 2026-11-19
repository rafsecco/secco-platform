using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Api.Identity;

namespace Secco.SecureGate.Api.Pages;

/// <summary>
/// Tela "Você saiu": destino do <c>/connect/authorize</c> quando a pessoa acabou de sair e o tenant
/// usa o login federado direto — sem ela, o Entra (ainda com sessão) a reconectaria sozinho. O botão
/// "Entrar" apaga a marca de saída e retoma o fluxo; o link leva ao login local (saída de emergência).
/// Anônima por design; antiforgery pelo Razor; só destinos locais (ADR-0020).
/// </summary>
[AllowAnonymous]
public sealed class SignedOutModel : PageModel
{
	/// <summary>Destino após "Entrar" (a requisição de autorização original). Local apenas.</summary>
	public string? ReturnUrl { get; private set; }

	/// <summary>Renderiza a tela.</summary>
	/// <param name="returnUrl">Destino local após entrar de novo.</param>
	public void OnGet(string? returnUrl = null)
	{
		// Valor não local nunca chega ao HTML: o POST também o descartaria
		ReturnUrl = FederatedFailureRedirect.IsLocal(returnUrl) ? returnUrl : null;
	}

	/// <summary>Apaga a marca de saída e volta ao destino local (ou à conta).</summary>
	/// <param name="returnUrl">Destino local após entrar de novo.</param>
	public IActionResult OnPost(string? returnUrl = null)
	{
		SignInMarkers.ClearSignedOut(HttpContext);

		return LocalRedirect(FederatedFailureRedirect.IsLocal(returnUrl) ? returnUrl! : "/conta");
	}
}
