using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Api.Extensions;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Infrastructure.Credentials;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Convite e redefinição têm janelas próprias (ADR-0033) porque cada um tem o seu tipo de options;
/// se compartilhassem, a validade longa do convite valeria também para o link de recuperação.
/// </summary>
/// <remarks>
/// A expiração em si é do <c>DataProtectorTokenProvider</c> do ASP.NET Identity, que compara o
/// instante gravado no token com a validade configurada. O que é nosso — e o que este teste
/// protege — é a ligação entre a configuração do produto e cada provedor.
/// </remarks>
public class CredentialTokenLifetimeTests
{
	private static ServiceProvider Build(int inviteHours, int resetMinutes)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
		services.AddSingleton(new CredentialOptions
		{
			PublicBaseUrl = "https://id.exemplo",
			InviteLifetimeHours = inviteHours,
			ResetLifetimeMinutes = resetMinutes,
		});
		services.AddSecureGateCredentials();

		return services.BuildServiceProvider();
	}

	[Fact]
	public void Provedores_UsamAsValidadesDaConfiguracao()
	{
		using var provider = Build(inviteHours: 5, resetMinutes: 7);

		provider.GetRequiredService<IOptions<InviteTokenProviderOptions>>().Value.TokenLifespan
			.Should().Be(TimeSpan.FromHours(5));
		provider.GetRequiredService<IOptions<ResetTokenProviderOptions>>().Value.TokenLifespan
			.Should().Be(TimeSpan.FromMinutes(7));
	}

	[Fact]
	public void Provedores_ComOsPadroes_TemJanelasDiferentes()
	{
		using var provider = Build(inviteHours: 72, resetMinutes: 30);

		var invite = provider.GetRequiredService<IOptions<InviteTokenProviderOptions>>().Value.TokenLifespan;
		var reset = provider.GetRequiredService<IOptions<ResetTokenProviderOptions>>().Value.TokenLifespan;

		invite.Should().Be(TimeSpan.FromHours(72));
		reset.Should().Be(TimeSpan.FromMinutes(30));
		reset.Should().BeLessThan(invite, "um link de recuperação vive muito menos que um convite");
	}

	[Fact]
	public void Provedores_TemNomesDistintos()
	{
		using var provider = Build(inviteHours: 72, resetMinutes: 30);
		var names = provider.GetRequiredService<CredentialTokenProviderNames>();

		// Nome e propósito distintos impedem que um token de convite seja aceito como
		// token de redefinição, e vice-versa.
		names.Invite.Should().NotBe(names.Reset);
		names.InvitePurpose.Should().NotBe(names.ResetPurpose);
	}
}
