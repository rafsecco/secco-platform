using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Infrastructure.Tenancy;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Escopo de acesso (ADR-0041): estados, pilha restaurável e argumentos inválidos.</summary>
public class TenantAccessScopeTests
{
	private static TenantAccessScope NewScope() => new(NullLogger<TenantAccessScope>.Instance);

	[Fact]
	public void Novo_Sempre_ComecaSemEscopo()
	{
		var scope = NewScope();

		scope.Mode.Should().Be(TenantAccessMode.None);
		scope.TenantId.Should().BeNull();
	}

	[Fact]
	public void EnterTenant_Sempre_ExpoeOTenantEVoltaAoDescartar()
	{
		var scope = NewScope();
		var tenantId = Guid.NewGuid();

		var handle = scope.EnterTenant(tenantId);

		scope.Mode.Should().Be(TenantAccessMode.Tenant);
		scope.TenantId.Should().Be(tenantId);

		handle.Dispose();

		scope.Mode.Should().Be(TenantAccessMode.None);
		scope.TenantId.Should().BeNull();
	}

	[Fact]
	public void EnterPlatform_Sempre_NaoTemTenantEVoltaAoDescartar()
	{
		var scope = NewScope();

		var handle = scope.EnterPlatform("motivo de teste");

		scope.Mode.Should().Be(TenantAccessMode.Platform);
		scope.TenantId.Should().BeNull();

		handle.Dispose();

		scope.Mode.Should().Be(TenantAccessMode.None);
	}

	[Fact]
	public void Aninhamento_PlataformaTenantPlataforma_RestauraExatamenteOEstadoAnterior()
	{
		var scope = NewScope();
		var tenantId = Guid.NewGuid();

		using (scope.EnterPlatform("externo"))
		{
			using (scope.EnterTenant(tenantId))
			{
				scope.Mode.Should().Be(TenantAccessMode.Tenant);
				scope.TenantId.Should().Be(tenantId);
			}

			scope.Mode.Should().Be(TenantAccessMode.Platform);
			scope.TenantId.Should().BeNull();
		}

		scope.Mode.Should().Be(TenantAccessMode.None);
	}

	[Fact]
	public void Aninhamento_TenantTenant_VoltaAoTenantExterno()
	{
		var scope = NewScope();
		var outer = Guid.NewGuid();
		var inner = Guid.NewGuid();

		using (scope.EnterTenant(outer))
		{
			using (scope.EnterTenant(inner))
			{
				scope.TenantId.Should().Be(inner);
			}

			scope.TenantId.Should().Be(outer);
		}
	}

	[Fact]
	public void Dispose_Repetido_NaoRestauraDeNovo()
	{
		var scope = NewScope();
		var tenantId = Guid.NewGuid();

		var outer = scope.EnterTenant(tenantId);
		var inner = scope.EnterPlatform("interno");

		inner.Dispose();
		outer.Dispose();
		inner.Dispose();

		scope.Mode.Should().Be(TenantAccessMode.None, "descartar duas vezes o mesmo handle não reabre um escopo já fechado");
	}

	[Fact]
	public void EnterTenant_GuidVazio_Lanca()
	{
		var scope = NewScope();

		var act = () => scope.EnterTenant(Guid.Empty);

		act.Should().Throw<ArgumentException>();
		scope.Mode.Should().Be(TenantAccessMode.None);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void EnterPlatform_MotivoVazio_Lanca(string? reason)
	{
		var scope = NewScope();

		var act = () => scope.EnterPlatform(reason!);

		act.Should().Throw<ArgumentException>();
		scope.Mode.Should().Be(TenantAccessMode.None);
	}
}
