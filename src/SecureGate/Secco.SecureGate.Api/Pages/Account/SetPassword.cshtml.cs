using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Tela que consome o link do convite e define a primeira senha (ADR-0033).
/// </summary>
/// <remarks>
/// Anônima por natureza — quem chega aqui ainda não tem credencial —, com antiforgery do Razor.
/// <b>Não existe endpoint de API equivalente</b>: um endpoint público que aceita token e senha é
/// convite a automação, e o que protege o link é justamente ele ser usado uma vez, por uma pessoa.
/// <para>
/// Link recusado nunca diz o motivo: expirado, já usado, conta desativada e conta de outro tenant
/// levam à mesma tela com a mesma mensagem (ADR-0020).
/// </para>
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="auditor">Trilha (best-effort).</param>
[AllowAnonymous]
public sealed class SetPasswordModel(ICredentialTokens tokens, ICredentialAuditor auditor) : PageModel
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

	/// <summary>Verdadeiro quando o link não vale mais — a tela oferece pedir outro.</summary>
	public bool LinkRejected { get; private set; }

	/// <summary>Mensagem de erro do formulário.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Senha e confirmação.</summary>
	public sealed class InputModel
	{
		/// <summary>Senha escolhida.</summary>
		[Required(ErrorMessage = "Informe a senha.")]
		// ADR-0020: teto contra amplificação de custo no hashing PBKDF2; o mínimo e a
		// complexidade são política do Identity.
		[StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
		[DataType(DataType.Password)]
		public string Password { get; set; } = string.Empty;

		/// <summary>Confirmação, para não gravar um erro de digitação como senha.</summary>
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

	/// <summary>Consome o link e define a senha.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
		{
			return Page();
		}

		var outcome = await tokens
			.SetPasswordAsync(UserId, Token, invite: true, Input.Password, cancellationToken)
			.ConfigureAwait(false);

		switch (outcome)
		{
			case CredentialTokenOutcome.Done:
				await AuditAsync(CredentialAuditEvent.PasswordSet, cancellationToken).ConfigureAwait(false);

				return RedirectToPage("/Login");

			case CredentialTokenOutcome.WeakPassword:
				ErrorMessage = "A senha não atende à política: use ao menos 8 caracteres, com maiúscula, minúscula, número e símbolo.";

				return Page();

			default:
				await AuditAsync(CredentialAuditEvent.LinkRejected, cancellationToken).ConfigureAwait(false);
				LinkRejected = true;

				return Page();
		}
	}

	private async Task AuditAsync(CredentialAuditEvent auditEvent, CancellationToken cancellationToken)
	{
		if (await tokens.FindAsync(UserId, cancellationToken).ConfigureAwait(false) is { } account)
		{
			await auditor.RecordAsync(auditEvent, account.UserId, account.TenantId, account.Email, cancellationToken)
				.ConfigureAwait(false);
		}
	}
}
