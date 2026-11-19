using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Abstractions;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Infrastructure.Federation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Sem o Entra configurado a política não consulta nada.</summary>
public class DirectSignInPolicyUnitTests
{
	[Fact]
	public async Task Resolve_EntraNaoConfigurado_NuloSemConsultarNada()
	{
		var applications = Substitute.For<IOpenIddictApplicationManager>();
		var tenants = Substitute.For<ITenantRepository>();
		var scope = Substitute.For<ITenantAccessScope>();
		var policy = new DirectSignInPolicy(
			applications, tenants, scope, Options.Create(new SecureGateEntraIdOptions()));

		var target = await policy.ResolveAsync("qualquer-client", CancellationToken.None);

		target.Should().BeNull();
		await applications.Received(0).FindByClientIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		await tenants.Received(0).GetFederationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}
}
