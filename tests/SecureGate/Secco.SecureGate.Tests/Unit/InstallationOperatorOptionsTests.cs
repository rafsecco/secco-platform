using FluentAssertions;
using Secco.SecureGate.Application.InstallationOperators;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>A configuração aperta o TTL e a janela de autenticação recente, nunca afrouxa (ADR-0049).</summary>
public class InstallationOperatorOptionsTests
{
	[Theory]
	[InlineData(10, 10)]
	[InlineData(60, 15)]
	[InlineData(0, 1)]
	[InlineData(-5, 1)]
	public void Janelas_FicamEntreUmMinutoEOTeto(int configured, int effective)
	{
		var options = new InstallationOperatorOptions { TokenLifetimeMinutes = configured, RecentAuthenticationMinutes = configured };

		options.EffectiveTokenLifetime.Should().Be(TimeSpan.FromMinutes(effective));
		options.EffectiveRecentAuthentication.Should().Be(TimeSpan.FromMinutes(effective));
	}
}
