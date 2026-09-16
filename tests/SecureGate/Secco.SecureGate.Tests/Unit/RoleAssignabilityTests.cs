using FluentAssertions;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Roles;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Quais perfis podem ter usuários como membros. Os reservados que são identidades SÓ de token
/// (ADR-0031) nunca podem: um usuário membro deles teria o read-set cross-tenant pelo nome.
/// </summary>
public class RoleAssignabilityTests
{
	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData(SecureGatePlatform.OperatorRole)]
	[InlineData("INSTALLATION-OPERATOR")]
	public void Atribuivel(string name) => RoleInputRules.IsAssignableToUsers(name).Should().BeTrue();

	[Theory]
	[InlineData(SecureGatePlatform.ElevatedLogReaderRole)]
	[InlineData(SecureGatePlatform.AuditorRole)]
	[InlineData(SecureGatePlatform.LegacyOperatorRole)]
	[InlineData("INSTALLATION-AUDITOR")]
	public void NaoAtribuivel(string name) => RoleInputRules.IsAssignableToUsers(name).Should().BeFalse();
}
