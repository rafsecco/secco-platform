using System.Text.Json;
using FluentAssertions;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.Channels;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.NotificationHub.Infrastructure.Channels;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>
/// Canais externos (ADR-0029): conjunto fechado, tradução por ferramenta e a validação de
/// destino que serve de defesa em profundidade contra SSRF.
/// </summary>
public class ExternalChannelTests
{
	[Fact]
	public void Channels_All_ContainsTeamsAndSlackAndNothingElse() =>
		NotificationHubChannels.All.Should().BeEquivalentTo(
			["email", "in_app", "teams", "slack"],
			"o conjunto é fechado: canal novo exige provider e ADR (ADR-0029)");

	[Fact]
	public void Channels_ExternallyConfigured_IsOnlyTeamsAndSlack() =>
		NotificationHubChannels.ExternallyConfigured.Should().BeEquivalentTo(["teams", "slack"]);

	[Fact]
	public void NotificationChannel_Email_IsZero() =>
		// A migration ExternalChannels adiciona ie_channel com defaultValue 0 para as linhas que
		// já existiam. Reordenar este enum faria notificação de e-mail antiga virar outra coisa.
		((int)NotificationChannel.Email).Should().Be(0);

	[Theory]
	[InlineData("https://prod-00.westus.logic.azure.com/workflows/abc/triggers/manual/paths/invoke")]
	[InlineData("https://hooks.slack.com/services/T000/B000/XXXX")]
	public void Destination_WhenPublicHttps_IsAccepted(string destination) =>
		ChannelDestinationRules.Validate(destination).IsSuccess.Should().BeTrue();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("nao-e-url")]
	public void Destination_WhenNotAnAbsoluteUrl_IsRefused(string? destination) =>
		ChannelDestinationRules.Validate(destination).IsFailure.Should().BeTrue();

	[Fact]
	public void Destination_WhenNotHttps_IsRefused() =>
		// O corpo carrega o conteúdo da notificação e a própria URL é segredo.
		ChannelDestinationRules.Validate("http://hooks.slack.com/services/T/B/X").Error
			.Should().Be(NotificationHubErrors.Channels.DestinationMustBeHttps);

	[Theory]
	[InlineData("https://127.0.0.1/webhook")]
	[InlineData("https://localhost/webhook")]
	[InlineData("https://10.0.0.5/webhook")]
	[InlineData("https://192.168.1.10/webhook")]
	[InlineData("https://172.16.0.1/webhook")]
	[InlineData("https://169.254.169.254/latest/meta-data")]
	[InlineData("https://intranet/webhook")]
	public void Destination_WhenHostIsReserved_IsRefused(string destination) =>
		// Defesa em profundidade: a barreira principal é a URL nunca vir do payload. Isto protege
		// contra um operador legítimo ser enganado a apontar para dentro da infraestrutura —
		// inclusive para o endpoint de metadados de nuvem.
		ChannelDestinationRules.Validate(destination).Error
			.Should().Be(NotificationHubErrors.Channels.DestinationHostNotAllowed);

	[Fact]
	public void TeamsProvider_TranslatesToAdaptiveCard()
	{
		var payload = JsonSerializer.Serialize(
			TeamsNotificationProvider.BuildAdaptiveCard("Manutenção programada", "O sistema ficará indisponível."));

		payload.Should().Contain("application/vnd.microsoft.card.adaptive");
		payload.Should().Contain("AdaptiveCard");
		payload.Should().Contain("Manuten\\u00E7\\u00E3o programada");
		payload.Should().Contain("O sistema ficar\\u00E1 indispon\\u00EDvel.");
	}

	[Fact]
	public void SlackProvider_TranslatesToBlocksWithTextFallback()
	{
		var payload = JsonSerializer.Serialize(
			SlackNotificationProvider.BuildMessage("Manutencao", "Corpo da mensagem"));

		payload.Should().Contain("\"blocks\"");
		payload.Should().Contain("mrkdwn");

		// O `text` é o que aparece em push e em cliente que não renderiza blocos — omiti-lo
		// degradaria exatamente o caso "urgente", que é o motivo da issue #13.
		payload.Should().Contain("\"text\":\"Manutencao\"");
	}

	[Fact]
	public void SlackProvider_WhenSubjectExceedsHeaderLimit_TruncatesOnlyTheHeader()
	{
		var payload = JsonSerializer.Serialize(
			SlackNotificationProvider.BuildMessage(new string('a', 300), "Corpo"));

		using var document = JsonDocument.Parse(payload);

		var header = document.RootElement.GetProperty("blocks")[0]
			.GetProperty("text").GetProperty("text").GetString()!;

		// O bloco header do Slack rejeita acima de 150 caracteres — passar disso faz a API
		// recusar a mensagem inteira.
		header.Length.Should().BeLessThanOrEqualTo(150);
		header.Should().EndWith("…");

		// O `text` de fallback NÃO é truncado: o limite dele é de dezenas de milhares de
		// caracteres, e cortá-lo perderia conteúdo sem necessidade.
		document.RootElement.GetProperty("text").GetString().Should().HaveLength(300);
	}

	[Fact]
	public async Task Dispatch_ToUnconfiguredExternalChannel_FailsWithClearError()
	{
		// O caso que mais vai acontecer na adoção: canal pedido antes de configurar o destino.
		// Falhar no despacho dá 400 ao chamador; deixar passar só falharia dentro do job.
		var handler = CreateDispatchHandler(out _, out var externalQueue, seedTeams: false);

		var result = await handler.HandleAsync(new DispatchNotificationCommand(
			UserId: null, Recipient: null, Title: "Titulo", Message: "Mensagem",
			Source: null, Type: null, Link: null, Channels: [NotificationHubChannels.Teams]));

		result.Error.Should().Be(NotificationHubErrors.Channels.NotConfiguredForTenant("teams"));
		externalQueue.Enqueued.Should().BeEmpty();
	}

	[Fact]
	public async Task Dispatch_ToConfiguredExternalChannel_CreatesDeliveryWithTheChannel()
	{
		var handler = CreateDispatchHandler(out var notifications, out var externalQueue, seedTeams: true);

		var result = await handler.HandleAsync(new DispatchNotificationCommand(
			UserId: null, Recipient: null, Title: "Titulo", Message: "Mensagem",
			Source: null, Type: null, Link: null, Channels: [NotificationHubChannels.Teams]));

		result.IsSuccess.Should().BeTrue();
		result.Value.ExternalNotificationIds.Should().ContainSingle();

		var delivery = notifications.Added.Should().ContainSingle().Subject;
		delivery.Channel.Should().Be(NotificationChannel.Teams);
		delivery.Recipient.Should().BeNull("canal externo tem destino na configuração, não no registro");
		delivery.Status.Should().Be(NotificationStatus.Pending);

		externalQueue.Enqueued.Should().ContainSingle();
	}

	private static DispatchNotificationHandler CreateDispatchHandler(
		out FakeNotificationRepositoryForChannels notifications,
		out FakeExternalChannelDispatchQueue externalQueue,
		bool seedTeams)
	{
		notifications = new FakeNotificationRepositoryForChannels();
		externalQueue = new FakeExternalChannelDispatchQueue();

		var channels = new FakeChannelConfigurationRepository();

		if (seedTeams)
		{
			channels.Seed("teams", "https://prod-00.westus.logic.azure.com/workflows/abc");
		}

		return new DispatchNotificationHandler(
			notifications,
			new FakeEmailDispatchQueue(),
			new FakeInAppRepositoryForChannels(),
			channels,
			externalQueue,
			new NotificationHubOptions());
	}
}
