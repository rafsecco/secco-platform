using FluentAssertions;
using Xunit;

namespace Secco.SDK.Email.Tests;

public class EmailOptionsTests
{
	[Fact]
	public void Options_ProviderPadrao_ESmtp() =>
		new SeccoEmailOptions().Provider.Should().Be(SeccoEmailProvider.Smtp);

	[Fact]
	public void Options_SmtpSemHost_Invalida()
	{
		var options = new SeccoEmailOptions { FromAddress = "no-reply@secco.local" };

		options.TryValidate("SecureGate:Email", out var error).Should().BeFalse();
		error.Should().Contain("SecureGate:Email:Host");
	}

	[Fact]
	public void Options_SendGridSemApiKey_Invalida()
	{
		var options = new SeccoEmailOptions
		{
			Provider = SeccoEmailProvider.SendGrid,
			FromAddress = "no-reply@secco.local",
		};

		options.TryValidate("NotificationHub:Email", out var error).Should().BeFalse();
		error.Should().Contain("NotificationHub:Email:ApiKey");
	}

	[Fact]
	public void Options_MensagemDeErro_NuncaCitaSegredo()
	{
		var options = new SeccoEmailOptions { Provider = SeccoEmailProvider.SendGrid, ApiKey = "SG.segredo" };

		options.TryValidate("SecureGate:Email", out var error).Should().BeFalse();
		error.Should().NotContain("SG.segredo");
	}
}
