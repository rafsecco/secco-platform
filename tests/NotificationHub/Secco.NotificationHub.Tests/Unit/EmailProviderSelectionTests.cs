using System.Net;
using FluentAssertions;
using NSubstitute;
using Secco.NotificationHub.Infrastructure.Email;
using SendGrid;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>
/// Seleção e validação do provider de e-mail (issue #14). O envio em si não é testado contra o
/// SendGrid real: o que importa aqui é a porta ser satisfeita e a falha virar exceção, porque é
/// disso que o <c>SendEmailJob</c> depende para marcar <c>Failed</c> e deixar o Hangfire repetir.
/// </summary>
public class EmailProviderSelectionTests
{
	private static NotificationHubEmailOptions SendGridOptions() => new()
	{
		Provider = NotificationHubEmailProvider.SendGrid,
		ApiKey = "SG.chave-de-teste",
		FromAddress = "no-reply@secco.local",
	};

	[Fact]
	public void Options_DefaultProviderIsSmtp() =>
		new NotificationHubEmailOptions().Provider.Should().Be(NotificationHubEmailProvider.Smtp);

	[Fact]
	public void Options_WhenSmtpWithoutHost_FailsValidation()
	{
		var options = new NotificationHubEmailOptions { FromAddress = "no-reply@secco.local" };

		options.TryValidate(out var error).Should().BeFalse();
		error.Should().Contain("Host");
	}

	[Fact]
	public void Options_WhenSendGridWithoutApiKey_FailsValidation()
	{
		var options = new NotificationHubEmailOptions
		{
			Provider = NotificationHubEmailProvider.SendGrid,
			FromAddress = "no-reply@secco.local",
		};

		options.TryValidate(out var error).Should().BeFalse();
		error.Should().Contain("ApiKey");
	}

	[Fact]
	public void Options_WhenSendGridConfigured_DoesNotRequireSmtpHost() =>
		SendGridOptions().TryValidate(out _).Should().BeTrue(
			"trocar de provider não pode exigir a configuração do provider que não se usa");

	[Fact]
	public void Options_WithoutFromAddress_FailsValidationForAnyProvider()
	{
		var options = new NotificationHubEmailOptions { Host = "smtp.interno" };

		options.TryValidate(out var error).Should().BeFalse();
		error.Should().Contain("FromAddress");
	}

	[Fact]
	public async Task SendGrid_WhenApiAccepts_DoesNotThrow()
	{
		var client = Substitute.For<ISendGridClient>();
		client.SendEmailAsync(Arg.Any<SendGrid.Helpers.Mail.SendGridMessage>(), Arg.Any<CancellationToken>())
			.Returns(new Response(HttpStatusCode.Accepted, null, null));

		var sender = new SendGridEmailSender(client, SendGridOptions());

		var act = async () => await sender.SendAsync("alguem@empresa.com", "Assunto", "Corpo", CancellationToken.None);

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task SendGrid_WhenApiRefuses_ThrowsWithoutLeakingTheBody()
	{
		var client = Substitute.For<ISendGridClient>();
		client.SendEmailAsync(Arg.Any<SendGrid.Helpers.Mail.SendGridMessage>(), Arg.Any<CancellationToken>())
			.Returns(new Response(HttpStatusCode.Forbidden, null, null));

		var sender = new SendGridEmailSender(client, SendGridOptions());

		var act = async () => await sender.SendAsync("alguem@empresa.com", "Assunto", "Corpo", CancellationToken.None);

		// Lançar é o contrato da porta: quem decide o retry é o job (ADR-0015).
		var exception = await act.Should().ThrowAsync<InvalidOperationException>();

		// O corpo da resposta pode ecoar o payload, e o payload é o conteúdo da notificação.
		exception.Which.Message.Should().Contain("403").And.NotContain("Corpo");
	}
}
