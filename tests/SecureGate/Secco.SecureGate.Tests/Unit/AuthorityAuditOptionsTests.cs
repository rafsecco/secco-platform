using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Secco.SecureGate.Application.TenantAdministration;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Retenção opt-in da trilha de autoridade (ADR-0054): sem valor não expira; fora de 1 a 3650 é ignorada.</summary>
public class AuthorityAuditOptionsTests
{
	[Fact]
	public void SemValor_NaoExpira()
	{
		var options = Bind(null);

		options.RetentionDays.Should().BeNull();
		options.IsRetentionInvalid.Should().BeFalse();
		options.EffectiveRetention.Should().BeNull();
	}

	[Theory]
	[InlineData("1", 1)]
	[InlineData("30", 30)]
	[InlineData("3650", 3650)]
	public void ValorNaFaixa_VirraJanela(string value, int days)
	{
		var options = Bind(value);

		options.IsRetentionInvalid.Should().BeFalse();
		options.EffectiveRetention.Should().Be(TimeSpan.FromDays(days));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("3651")]
	[InlineData("100000")]
	public void ValorForaDaFaixa_EIgnorado_ENaoHaJanela(string value)
	{
		var options = Bind(value);

		options.IsRetentionInvalid.Should().BeTrue();
		options.EffectiveRetention.Should().BeNull("valor inválido nunca apaga");
	}

	[Fact]
	public void Secao_EAMesmaDaIdentidadeDeAuditoria()
	{
		AuthorityAuditOptions.SectionKey.Should().Be("SecureGate:Audit");
	}

	private static AuthorityAuditOptions Bind(string? retentionDays)
	{
		var settings = new Dictionary<string, string?>();

		if (retentionDays is not null)
		{
			settings[$"{AuthorityAuditOptions.SectionKey}:RetentionDays"] = retentionDays;
		}

		var options = new AuthorityAuditOptions();
		new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
			.GetSection(AuthorityAuditOptions.SectionKey).Bind(options);

		return options;
	}
}
