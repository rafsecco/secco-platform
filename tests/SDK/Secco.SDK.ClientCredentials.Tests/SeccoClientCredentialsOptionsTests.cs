using FluentAssertions;
using Xunit;

namespace Secco.SDK.ClientCredentials.Tests;

/// <summary>
/// <see cref="SeccoClientCredentialsOptions"/>: ausência total das três chaves é um modo
/// válido (sem autenticação, DEV); presença PARCIAL falha rápido (ADR-0020) listando o que
/// falta e citando a seção de origem.
/// </summary>
public class SeccoClientCredentialsOptionsTests
{
	[Fact]
	public void IsConfigured_AllKeysEmpty_ReturnsFalse()
	{
		var options = new SeccoClientCredentialsOptions();

		options.IsConfigured.Should().BeFalse();
	}

	[Fact]
	public void IsConfigured_OnlyOneKeyPresent_ReturnsTrue()
	{
		var options = new SeccoClientCredentialsOptions { ClientId = "algum-client" };

		options.IsConfigured.Should().BeTrue();
	}

	[Fact]
	public void Validate_PartialConfiguration_ThrowsListingMissingKeysAndSection()
	{
		var options = new SeccoClientCredentialsOptions
		{
			SectionKey = "Secco:Teste",
			AuthorityUrl = "https://issuer.test",
			// ClientId e ClientSecret ausentes
		};

		var act = options.Validate;

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*ClientId*ClientSecret*")
			.WithMessage("*Secco:Teste*");
	}

	[Fact]
	public void Validate_AllKeysMissing_ListsAllThree()
	{
		var options = new SeccoClientCredentialsOptions();

		var act = options.Validate;

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*AuthorityUrl*")
			.WithMessage("*ClientId*")
			.WithMessage("*ClientSecret*");
	}

	[Fact]
	public void Validate_FullConfiguration_DoesNotThrow()
	{
		var options = new SeccoClientCredentialsOptions
		{
			AuthorityUrl = "https://issuer.test",
			ClientId = "client",
			ClientSecret = "secret",
		};

		var act = options.Validate;

		act.Should().NotThrow();
	}

	[Fact]
	public void Validate_AuthorityUrlNotAbsolute_Throws()
	{
		var options = new SeccoClientCredentialsOptions
		{
			AuthorityUrl = "/caminho/relativo",
			ClientId = "client",
			ClientSecret = "secret",
		};

		var act = options.Validate;

		act.Should().Throw<InvalidOperationException>().WithMessage("*URL http(s) absoluta*");
	}

	[Fact]
	public void Validate_AuthorityUrlWithInvalidScheme_Throws()
	{
		var options = new SeccoClientCredentialsOptions
		{
			AuthorityUrl = "ftp://issuer.test",
			ClientId = "client",
			ClientSecret = "secret",
		};

		var act = options.Validate;

		act.Should().Throw<InvalidOperationException>().WithMessage("*URL http(s) absoluta*");
	}
}
