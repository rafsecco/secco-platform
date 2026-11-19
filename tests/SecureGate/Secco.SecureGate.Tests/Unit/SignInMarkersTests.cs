using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Secco.SecureGate.Api.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Marcas do login federado direto: nenhuma concede acesso, só evitam laço de redirecionamento.</summary>
public class SignInMarkersTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

	private static DefaultHttpContext NewContext(string environmentName = "Testing")
	{
		var environment = Substitute.For<IHostEnvironment>();
		environment.EnvironmentName.Returns(environmentName);

		var services = new ServiceCollection().AddSingleton(environment).BuildServiceProvider();

		return new DefaultHttpContext { RequestServices = services };
	}

	private static DefaultHttpContext WithCookie(string value)
	{
		var context = NewContext();
		context.Request.Headers.Cookie = $"{SignInMarkers.SignedOutCookie}={value}";

		return context;
	}

	[Fact]
	public void MarkSignedOut_GravaCookieHttpOnlyLaxSemExpires()
	{
		var context = NewContext();

		SignInMarkers.MarkSignedOut(context, Now);

		var header = context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
		header.Should().Contain(SignInMarkers.SignedOutCookie);
		header.Should().Contain("httponly");
		header.Should().Contain("samesite=lax");
		header.Should().NotContain("expires");
	}

	[Fact]
	public void MarkSignedOut_EmProducao_Secure()
	{
		var context = NewContext("Production");

		SignInMarkers.MarkSignedOut(context, Now);

		context.Response.Headers.SetCookie.ToString().ToLowerInvariant().Should().Contain("secure");
	}

	[Fact]
	public void HasSignedOut_DentroDe12h_Verdadeiro()
	{
		var context = WithCookie(Now.AddHours(-11).ToUnixTimeSeconds().ToString());

		SignInMarkers.HasSignedOut(context, Now).Should().BeTrue();
	}

	[Fact]
	public void HasSignedOut_Com12hE1s_Falso()
	{
		var context = WithCookie(Now.AddHours(-12).AddSeconds(-1).ToUnixTimeSeconds().ToString());

		SignInMarkers.HasSignedOut(context, Now).Should().BeFalse();
	}

	[Fact]
	public void HasSignedOut_ValorNaoNumerico_Falso()
	{
		SignInMarkers.HasSignedOut(WithCookie("abc"), Now).Should().BeFalse();
	}

	[Fact]
	public void HasSignedOut_SemCookie_Falso()
	{
		SignInMarkers.HasSignedOut(NewContext(), Now).Should().BeFalse();
	}

	[Fact]
	public void WithDirectAttempt_PreservaAQueryOriginal()
	{
		var url = SignInMarkers.WithDirectAttempt("/connect/authorize?client_id=x&state=y");

		url.Should().Contain("client_id=x");
		url.Should().Contain("state=y");
		url.Should().Contain("secco_direct_attempt=1");
	}

	[Theory]
	[InlineData("?secco_direct_attempt=1", true)]
	[InlineData("?secco_direct_attempt=0", false)]
	[InlineData("?secco_direct_attempt=true", false)]
	[InlineData("?outro=1", false)]
	[InlineData("", false)]
	public void HasDirectAttempt_SoComValor1(string query, bool expected)
	{
		var context = NewContext();
		context.Request.QueryString = new QueryString(query);

		SignInMarkers.HasDirectAttempt(context.Request).Should().Be(expected);
	}
}
