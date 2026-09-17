using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;
using Secco.AdminPortal.Authentication;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// O access token do operador vive num cofre no servidor, e o cookie só leva o id da sessão (ADR-0032). O
/// provider renova perto do vencimento, uma vez por sessão, e manda para novo login quando não dá.
/// </summary>
public class OperatorTokenProviderTests
{
	private const string SessionId = "sessao-1";

	private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
	{
		public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
			Task.FromResult(new AuthenticationState(user));
	}

	private sealed class RecordingNavigation : NavigationManager
	{
		public string? ForcedTo { get; private set; }

		public RecordingNavigation() => Initialize("https://portal.test/", "https://portal.test/tenants");

		protected override void NavigateToCore(string uri, NavigationOptions options) =>
			ForcedTo = options.ForceLoad ? uri : null;
	}

	private static ClaimsPrincipal Principal() =>
		new(new ClaimsIdentity([new Claim(AdminPortalDefaults.SessionIdClaim, SessionId)], "test"));

	private static (OperatorTokenProvider Provider, IOperatorSessionStore Store, IOperatorTokenRefresher Refresher, RecordingNavigation Navigation)
		Build(OperatorSession? stored)
	{
		var store = Substitute.For<IOperatorSessionStore>();
		store.GetAsync(SessionId, Arg.Any<CancellationToken>()).Returns(stored);
		var refresher = Substitute.For<IOperatorTokenRefresher>();
		var navigation = new RecordingNavigation();

		return (new OperatorTokenProvider(new StubAuthStateProvider(Principal()), store, refresher, navigation), store, refresher, navigation);
	}

	[Fact]
	public async Task TokenValido_DevolveSemRenovar()
	{
		var (provider, _, refresher, _) = Build(new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddMinutes(4)));

		(await provider.GetAccessTokenAsync()).Should().Be("a1");
		await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
	}

	[Fact]
	public async Task PertoDoVencimento_RenovaEGrava()
	{
		var (provider, store, refresher, _) = Build(new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddSeconds(30)));
		var renewed = new OperatorSession("a2", "r2", DateTimeOffset.UtcNow.AddMinutes(5));
		refresher.RefreshAsync("r1", Arg.Any<CancellationToken>()).Returns(renewed);

		(await provider.GetAccessTokenAsync()).Should().Be("a2");
		await store.Received(1).SetAsync(SessionId, renewed, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ChamadasSimultaneas_UmaRenovacao()
	{
		var session = new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddSeconds(30));
		var renewed = new OperatorSession("a2", "r2", DateTimeOffset.UtcNow.AddMinutes(5));
		var store = Substitute.For<IOperatorSessionStore>();
		var current = session;
		store.GetAsync(SessionId, Arg.Any<CancellationToken>()).Returns(_ => current);
		store.When(s => s.SetAsync(SessionId, Arg.Any<OperatorSession>(), Arg.Any<CancellationToken>()))
			.Do(call => current = call.ArgAt<OperatorSession>(1));
		var refresher = Substitute.For<IOperatorTokenRefresher>();
		refresher.RefreshAsync("r1", Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			await Task.Delay(50);
			return renewed;
		});
		var provider = new OperatorTokenProvider(new StubAuthStateProvider(Principal()), store, refresher, new RecordingNavigation());

		var tokens = await Task.WhenAll(provider.GetAccessTokenAsync(), provider.GetAccessTokenAsync());

		tokens.Should().OnlyContain(token => token == "a2");
		await refresher.Received(1).RefreshAsync("r1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task RenovacaoRecusada_RemoveSessaoEForcaNovoLogin()
	{
		var (provider, store, refresher, navigation) = Build(new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddSeconds(30)));
		refresher.RefreshAsync("r1", Arg.Any<CancellationToken>()).Returns((OperatorSession?)null);

		(await provider.GetAccessTokenAsync()).Should().BeNull();
		await store.Received(1).RemoveAsync(SessionId, Arg.Any<CancellationToken>());
		navigation.ForcedTo.Should().Be("https://portal.test/tenants");
	}

	[Fact]
	public async Task SessaoAusenteNoCofre_ForcaNovoLogin()
	{
		var (provider, _, _, navigation) = Build(stored: null);

		(await provider.GetAccessTokenAsync()).Should().BeNull();
		navigation.ForcedTo.Should().NotBeNull();
	}
}
