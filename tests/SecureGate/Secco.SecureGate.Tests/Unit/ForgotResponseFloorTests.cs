using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;
using Secco.SecureGate.Api.Pages.Account;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Credentials;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Piso de tempo da resposta do "esqueci minha senha" (ADR-0033/ADR-0020).
/// </summary>
/// <remarks>
/// A mensagem idêntica impede descobrir quais e-mails têm conta; o relógio, não. Só o caso "a
/// conta existe" envia e-mail, e envio leva tempo — sem um piso, um cronômetro faz o trabalho
/// que a mensagem genérica impede.
/// </remarks>
public class ForgotResponseFloorTests
{
	private const int FloorMilliseconds = 400;

	private static ForgotModel Build()
	{
		var tokens = Substitute.For<ICredentialTokens>();
		var throttle = Substitute.For<IPasswordResetThrottle>();
		throttle.TryAcquire(Arg.Any<string>(), Arg.Any<string?>()).Returns(true);

		// Conta inexistente: o caminho MAIS RÁPIDO do caso de uso, que é justamente o que o piso
		// precisa igualar ao caminho lento (gerar token e enviar e-mail).
		tokens.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns((CredentialAccount?)null);

		var handler = new RequestPasswordResetHandler(
			tokens,
			Substitute.For<ICredentialMailer>(),
			Substitute.For<ICredentialAuditor>(),
			throttle);

		return new ForgotModel(handler, new CredentialOptions
		{
			PublicBaseUrl = "https://id.exemplo",
			ResponseFloorMilliseconds = FloorMilliseconds,
		})
		{
			PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
			Input = new ForgotModel.InputModel { Email = "ninguem@exemplo.com" },
		};
	}

	[Fact]
	public async Task Esqueci_ContaInexistente_RespeitaOPisoDeTempo()
	{
		var model = Build();
		var started = Stopwatch.GetTimestamp();

		await model.OnPostAsync(CancellationToken.None);

		// Margem para a granularidade do timer do sistema; o que importa é não responder na hora.
		Stopwatch.GetElapsedTime(started).Should().BeGreaterThanOrEqualTo(
			TimeSpan.FromMilliseconds(FloorMilliseconds - 50));
	}

	[Fact]
	public async Task Esqueci_SempreResponde200ComAMesmaTela()
	{
		var model = Build();

		var result = await model.OnPostAsync(CancellationToken.None);

		result.Should().BeOfType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>();
		model.Submitted.Should().BeTrue();
	}
}
