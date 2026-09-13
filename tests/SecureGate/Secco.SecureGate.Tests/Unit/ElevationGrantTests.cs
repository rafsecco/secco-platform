using FluentAssertions;
using Secco.SecureGate.Domain.Elevation;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Invariantes da <see cref="ElevationGrant"/> (ADR-0031): sem linha, não há elevação — é a
/// AUTORIDADE que o token exchange do núcleo consulta via <see cref="ElevationGrant.IsActiveAt"/>.
/// </summary>
public class ElevationGrantTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Constructor_WithEmptyUserId_Throws()
	{
		var act = () => new ElevationGrant(Guid.Empty, Guid.NewGuid(), "admin-sub", expiresAt: null, Now);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Constructor_WithEmptyTenantId_Throws()
	{
		var act = () => new ElevationGrant(Guid.NewGuid(), Guid.Empty, "admin-sub", expiresAt: null, Now);

		act.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Constructor_WithoutGrantedBy_Throws(string? grantedBy)
	{
		var act = () => new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), grantedBy!, expiresAt: null, Now);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Constructor_WithGrantedByAboveLimit_Throws()
	{
		var tooLong = new string('a', ElevationGrant.GrantedByMaxLength + 1);

		var act = () => new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), tooLong, expiresAt: null, Now);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Constructor_WithExpiresAtInThePast_Throws()
	{
		var act = () => new ElevationGrant(
			Guid.NewGuid(), Guid.NewGuid(), "admin-sub", Now.AddMinutes(-1), Now);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Constructor_WithExpiresAtEqualToNow_Throws()
	{
		var act = () => new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", Now, Now);

		act.Should().Throw<DomainInvariantException>("uma concessão que já nasce expirada não é uma concessão");
	}

	[Fact]
	public void Constructor_WithoutExpiresAt_CreatesGrantWithoutExpiration()
	{
		var userId = Guid.NewGuid();
		var tenantId = Guid.NewGuid();

		var grant = new ElevationGrant(userId, tenantId, "admin-sub", expiresAt: null, Now);

		grant.UserId.Should().Be(userId);
		grant.TenantId.Should().Be(tenantId);
		grant.GrantedBy.Should().Be("admin-sub");
		grant.CreatedAt.Should().Be(Now);
		grant.ExpiresAt.Should().BeNull();
	}

	[Fact]
	public void Constructor_WithFutureExpiresAt_CreatesGrantWithExpiration()
	{
		var expiresAt = Now.AddMinutes(15);

		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", expiresAt, Now);

		grant.ExpiresAt.Should().Be(expiresAt);
	}

	[Fact]
	public void IsActiveAt_WithoutExpiration_IsAlwaysActive()
	{
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", expiresAt: null, Now);

		grant.IsActiveAt(Now.AddYears(10)).Should().BeTrue("nulo significa sem expiração");
	}

	[Fact]
	public void IsActiveAt_BeforeExpiration_IsActive()
	{
		var expiresAt = Now.AddMinutes(15);
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", expiresAt, Now);

		grant.IsActiveAt(expiresAt.AddMinutes(-1)).Should().BeTrue();
	}

	[Fact]
	public void IsActiveAt_ExactlyAtExpiration_IsInactive()
	{
		var expiresAt = Now.AddMinutes(15);
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", expiresAt, Now);

		grant.IsActiveAt(expiresAt).Should().BeFalse("o instante exato de expiração já não é mais ativo");
	}

	[Fact]
	public void IsActiveAt_AfterExpiration_IsInactive()
	{
		var expiresAt = Now.AddMinutes(15);
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", expiresAt, Now);

		grant.IsActiveAt(expiresAt.AddMinutes(1)).Should().BeFalse();
	}

	[Fact]
	public void Renew_WithoutGrantedBy_Throws()
	{
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", expiresAt: null, Now);

		var act = () => grant.Renew(expiresAt: null, grantedBy: string.Empty, Now);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Renew_WithExpiresAtInThePast_Throws()
	{
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", expiresAt: null, Now);

		var act = () => grant.Renew(Now.AddMinutes(-1), "admin-sub", Now);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Renew_WithValidArguments_ReplacesGrantedByAndExpiresAt()
	{
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", Now.AddMinutes(5), Now);
		var newExpiresAt = Now.AddHours(1);

		grant.Renew(newExpiresAt, "another-admin-sub", Now);

		grant.GrantedBy.Should().Be("another-admin-sub");
		grant.ExpiresAt.Should().Be(newExpiresAt);
		grant.CreatedAt.Should().Be(Now, "a renovação não é uma nova concessão — a data original se mantém");
	}

	[Fact]
	public void Renew_ToNoExpiration_ClearsExpiresAt()
	{
		var grant = new ElevationGrant(Guid.NewGuid(), Guid.NewGuid(), "admin-sub", Now.AddMinutes(5), Now);

		grant.Renew(expiresAt: null, "admin-sub", Now);

		grant.ExpiresAt.Should().BeNull();
	}
}
