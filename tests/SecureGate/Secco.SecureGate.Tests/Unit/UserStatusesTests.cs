using FluentAssertions;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Derivação do status exibido ao admin a partir do lockout do Identity. A desativação grava
/// <see cref="DateTimeOffset.MaxValue"/>; bloqueio por tentativas grava uma data próxima.
/// </summary>
public class UserStatusesTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void SemData_Ativo() =>
		UserStatuses.From(lockoutEnabled: true, lockoutEnd: null, Now).Should().Be(UserStatuses.Active);

	[Fact]
	public void DataNoPassado_Ativo() =>
		UserStatuses.From(lockoutEnabled: true, Now.AddMinutes(-1), Now).Should().Be(UserStatuses.Active);

	[Fact]
	public void LockoutDesligado_AtivoMesmoComDataFutura() =>
		UserStatuses.From(lockoutEnabled: false, DateTimeOffset.MaxValue, Now).Should().Be(UserStatuses.Active);

	[Fact]
	public void DataMaxima_Desativado() =>
		UserStatuses.From(lockoutEnabled: true, DateTimeOffset.MaxValue, Now).Should().Be(UserStatuses.Deactivated);

	[Fact]
	public void DataMaximaTruncadaPeloBanco_ContinuaDesativado()
	{
		// O PostgreSQL guarda microssegundos: o MaxValue volta com o último tick a menos
		var truncated = DateTimeOffset.MaxValue.AddTicks(-9);

		UserStatuses.From(lockoutEnabled: true, truncated, Now).Should().Be(UserStatuses.Deactivated);
	}

	[Fact]
	public void DataProximaNoFuturo_Bloqueado() =>
		UserStatuses.From(lockoutEnabled: true, Now.AddMinutes(5), Now).Should().Be(UserStatuses.LockedOut);
}
