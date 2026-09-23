using FluentAssertions;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Exigência de segundo fator para o operador de instalação (entrega D, ADR-0030).
/// </summary>
/// <remarks>
/// A regra é por papel <b>no tenant de plataforma</b>: o nome do papel é único só por tenant, e um
/// papel homônimo num tenant de cliente não pode ganhar nem a exigência nem a isenção — é a mesma
/// defesa contra colisão de nome das ADR-0023/0024.
/// </remarks>
public class OperatorTwoFactorPolicyTests
{
	private static User Operador() => new() { Id = Guid.CreateVersion7(), TenantId = SecureGatePlatform.TenantId };

	private static User Comum() => new() { Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7() };

	[Fact]
	public void Operador_Sem2Fa_PrecisaCadastrar() =>
		OperatorTwoFactorPolicy.RequiresEnrollment(Operador(), [SecureGatePlatform.OperatorRole], twoFactorEnabled: false)
			.Should().BeTrue();

	[Fact]
	public void Operador_Com2Fa_NaoPrecisa() =>
		OperatorTwoFactorPolicy.RequiresEnrollment(Operador(), [SecureGatePlatform.OperatorRole], twoFactorEnabled: true)
			.Should().BeFalse();

	[Fact]
	public void UsuarioComum_Sem2Fa_NaoPrecisa() =>
		OperatorTwoFactorPolicy.RequiresEnrollment(Comum(), ["leitor"], twoFactorEnabled: false)
			.Should().BeFalse();

	[Fact]
	public void PapelHomonimoEmOutroTenant_NaoExige() =>
		OperatorTwoFactorPolicy.RequiresEnrollment(Comum(), [SecureGatePlatform.OperatorRole], twoFactorEnabled: false)
			.Should().BeFalse();

	[Fact]
	public void Operador_NaoPodeDesligarOProprio() =>
		OperatorTwoFactorPolicy.CanDisable(Operador(), [SecureGatePlatform.OperatorRole]).Should().BeFalse();

	[Fact]
	public void UsuarioComum_PodeDesligar() =>
		OperatorTwoFactorPolicy.CanDisable(Comum(), ["leitor"]).Should().BeTrue();

	[Fact]
	public void PapelHomonimoEmOutroTenant_PodeDesligar() =>
		// Se a colisão de nome bloqueasse o desligamento, um tenant de cliente conseguiria
		// prender os próprios usuários a um 2FA que ninguém ali pode remover.
		OperatorTwoFactorPolicy.CanDisable(Comum(), [SecureGatePlatform.OperatorRole]).Should().BeTrue();
}
