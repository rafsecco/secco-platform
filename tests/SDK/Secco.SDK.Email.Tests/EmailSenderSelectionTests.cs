using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SendGrid;
using SendGrid.Helpers.Mail;
using Xunit;

namespace Secco.SDK.Email.Tests;

public class EmailSenderSelectionTests
{
	private static ServiceProvider Build(Dictionary<string, string?> settings)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddSeccoEmail("SecureGate:Email");

		return services.BuildServiceProvider();
	}

	[Fact]
	public void Composicao_ProviderSendGrid_ResolveOAdaptadorDoSendGrid()
	{
		using var provider = Build(new Dictionary<string, string?>
		{
			["SecureGate:Email:Provider"] = "SendGrid",
			["SecureGate:Email:ApiKey"] = "SG.chave-de-teste",
			["SecureGate:Email:FromAddress"] = "no-reply@secco.local",
		});

		provider.GetRequiredService<ISeccoEmailSender>().Should().BeOfType<SeccoSendGridEmailSender>();
	}

	[Fact]
	public void Composicao_ProviderPadrao_ResolveOAdaptadorSmtp()
	{
		using var provider = Build(new Dictionary<string, string?>
		{
			["SecureGate:Email:Host"] = "localhost",
			["SecureGate:Email:FromAddress"] = "no-reply@secco.local",
		});

		provider.GetRequiredService<ISeccoEmailSender>().Should().BeOfType<SeccoSmtpEmailSender>();
	}

	[Fact]
	public async Task SendGrid_QuandoAApiRecusa_Lanca()
	{
		var client = Substitute.For<ISendGridClient>();
		client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
			.Returns(new Response(HttpStatusCode.BadRequest, new StringContent("payload secreto"), null));

		var sender = new SeccoSendGridEmailSender(client, new SeccoEmailOptions { FromAddress = "no-reply@secco.local" });

		var act = async () => await sender.SendAsync("alguem@secco.local", "Assunto", "Corpo", CancellationToken.None);

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.Which.Message.Should().NotContain("payload secreto");
	}
}
