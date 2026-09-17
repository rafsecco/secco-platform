using FluentAssertions;
using Secco.SecureGate.Application.Sessions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>A versão de sessão resume o SecurityStamp sem expô-lo.</summary>
public class SessionVersionTests
{
	[Fact]
	public void MesmoStamp_MesmaVersao() =>
		SessionVersion.From("ABC").Should().Be(SessionVersion.From("ABC"));

	[Fact]
	public void StampDiferente_VersaoDiferente() =>
		SessionVersion.From("ABC").Should().NotBe(SessionVersion.From("ABD"));

	[Fact]
	public void Formato_16CaracteresBase64UrlSemOStamp()
	{
		var version = SessionVersion.From("STAMP-SECRETO-1234");

		version.Should().HaveLength(16).And.MatchRegex("^[A-Za-z0-9_-]{16}$").And.NotContain("STAMP");
	}

	[Fact]
	public void StampNulo_TemVersaoEstavel() =>
		SessionVersion.From(null).Should().Be(SessionVersion.From(string.Empty));
}
