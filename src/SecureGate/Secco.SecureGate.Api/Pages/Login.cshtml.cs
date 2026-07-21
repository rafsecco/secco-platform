using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Pages;

/// <summary>
/// Tela de login (Fase 6.5): autentica o usuário pela senha (hash do Identity) e cria o
/// cookie de sessão; o endpoint <c>/connect/authorize</c> retoma o fluxo OIDC a partir do
/// <c>ReturnUrl</c>. Com a federação configurada (ADR-0026), oferece também o login pela
/// conta corporativa (Entra ID). Anônima por design (é a porta de entrada); antiforgery
/// pelo Razor.
/// </summary>
[AllowAnonymous]
public sealed class LoginModel(
    SignInManager<User> signInManager,
    IOptions<SecureGateEntraIdOptions> entraOptions) : PageModel
{
    /// <summary>Mensagem genérica de recusa federada — nunca revela o motivo real (ADR-0020).</summary>
    private const string FederatedErrorMessage =
        "Não foi possível autenticar com a conta corporativa. Verifique com o administrador do seu tenant.";

    /// <summary>Login federado (Entra ID) disponível nesta instalação (ADR-0026).</summary>
    public bool EntraEnabled => entraOptions.Value.IsConfigured;
    /// <summary>Credenciais submetidas pelo formulário.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Destino após o login (a requisição de autorização OIDC original). Local apenas.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Mensagem de erro exibida após uma tentativa malsucedida.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Campos do formulário de login.</summary>
    public sealed class InputModel
    {
        /// <summary>E-mail (username) do usuário.</summary>
        [Required(ErrorMessage = "Informe o e-mail.")]
        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        public string Email { get; set; } = string.Empty;

        /// <summary>Senha do usuário.</summary>
        [Required(ErrorMessage = "Informe a senha.")]
        // ADR-0020: limite superior contra amplificação de custo no hashing PBKDF2 do Identity
        // (DoS) — a política de mínimo/complexidade é do Identity, isto é só um teto.
        [StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Renderiza o formulário.</summary>
    /// <param name="returnUrl">Destino local após o login.</param>
    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (Request.Query.ContainsKey("federatedError"))
        {
            ErrorMessage = FederatedErrorMessage;
        }
    }

    /// <summary>
    /// Inicia o login federado (ADR-0026): desafia o esquema do Entra ID; o callback
    /// <c>/login/entra-callback</c> decide fail-closed e cria a sessão.
    /// </summary>
    /// <param name="returnUrl">Destino local após o login.</param>
    public IActionResult OnPostEntra(string? returnUrl = null)
    {
        if (!EntraEnabled)
        {
            return NotFound();
        }

        // Só destinos locais viajam no fluxo — sem open redirect (ADR-0020)
        var target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

        var properties = new AuthenticationProperties
        {
            RedirectUri = "/login/entra-callback?returnUrl=" + Uri.EscapeDataString(target),
        };

        return Challenge(properties, SecureGateEntraIdOptions.AuthenticationScheme);
    }

    /// <summary>Processa a submissão do formulário.</summary>
    /// <param name="returnUrl">Destino local após o login.</param>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // lockoutOnFailure: alimenta o bloqueio contra força bruta (ADR-0020)
        var result = await signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // LocalRedirect: recusa URLs absolutas — sem open redirect (ADR-0020)
            return LocalRedirect(returnUrl ?? "/");
        }

        // Mensagem genérica: não revela se o e-mail existe (ADR-0020)
        ErrorMessage = result.IsLockedOut
            ? "Conta temporariamente bloqueada por excesso de tentativas. Tente novamente em alguns minutos."
            : "E-mail ou senha inválidos.";

        return Page();
    }
}
