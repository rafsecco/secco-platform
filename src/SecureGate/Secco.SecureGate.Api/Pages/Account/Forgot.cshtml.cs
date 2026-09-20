using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Credentials;

namespace Secco.SecureGate.Api.Pages.Account;

/// <summary>
/// Formulário público de recuperação de conta (ADR-0033).
/// </summary>
/// <remarks>
/// <b>A resposta é sempre a mesma</b>, exista a conta ou não, esteja ela ativa ou não, dentro ou
/// fora do limite de envios. Duas consequências práticas disso, ambas deliberadas: o usuário
/// legítimo não recebe aviso de que digitou o e-mail errado, e o atacante não consegue usar este
/// formulário para descobrir quem tem conta na instalação (ADR-0020).
/// <para>
/// O tempo também não pode denunciar: só o caso "a conta existe" envia e-mail, e envio leva tempo.
/// Por isso a resposta tem um piso configurável — sem ele, um cronômetro faria o trabalho que a
/// mensagem idêntica impede.
/// </para>
/// </remarks>
/// <param name="handler">Caso de uso do pedido de recuperação.</param>
/// <param name="options">Configuração de credencial (piso de tempo da resposta).</param>
[AllowAnonymous]
public sealed class ForgotModel(RequestPasswordResetHandler handler, CredentialOptions options) : PageModel
{
	/// <summary>Campos do formulário.</summary>
	[BindProperty]
	public InputModel Input { get; set; } = new();

	/// <summary>Verdadeiro depois do envio — a tela passa a mostrar a confirmação genérica.</summary>
	public bool Submitted { get; private set; }

	/// <summary>E-mail informado.</summary>
	public sealed class InputModel
	{
		/// <summary>E-mail da conta a recuperar.</summary>
		[Required(ErrorMessage = "Informe o e-mail.")]
		[EmailAddress(ErrorMessage = "E-mail inválido.")]
		[StringLength(256, ErrorMessage = "E-mail muito longo.")]
		public string Email { get; set; } = string.Empty;
	}

	/// <summary>Renderiza o formulário.</summary>
	public void OnGet()
	{
	}

	/// <summary>Dispara o envio, quando houver o que enviar, e responde sempre igual.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
		{
			return Page();
		}

		var started = Stopwatch.GetTimestamp();

		await handler
			.HandleAsync(Input.Email, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken)
			.ConfigureAwait(false);

		await ApplyResponseFloorAsync(started, cancellationToken).ConfigureAwait(false);

		Submitted = true;

		return Page();
	}

	/// <summary>Completa o piso de tempo, para o relógio não distinguir os casos.</summary>
	private async Task ApplyResponseFloorAsync(long startedAt, CancellationToken cancellationToken)
	{
		var floor = TimeSpan.FromMilliseconds(options.ResponseFloorMilliseconds);
		var elapsed = Stopwatch.GetElapsedTime(startedAt);

		if (elapsed < floor)
		{
			await Task.Delay(floor - elapsed, cancellationToken).ConfigureAwait(false);
		}
	}
}
