using FluentAssertions;
using Secco.LogStream.Infrastructure.Retention;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class RetentionPolicyTests
{
	[Fact]
	public void ResolveDays_WithTenantOverride_WinsOverDefault()
	{
		var tenantId = Guid.NewGuid();
		var options = new LogStreamRetentionOptions { DefaultDays = 30 };
		options.DaysByTenant[tenantId] = 90;

		RetentionPolicy.ResolveDays(options, tenantId).Should().Be(90);
		RetentionPolicy.ResolveDays(options, Guid.NewGuid()).Should().Be(30);
	}

	[Fact]
	public void ResolveDays_WithoutAnyConfiguration_ReturnsNull()
	{
		RetentionPolicy.ResolveDays(new LogStreamRetentionOptions(), Guid.NewGuid())
			.Should().BeNull("sem opt-in explícito, nada é expurgado");
	}

	[Theory]
	[InlineData(0, 6, false)]
	[InlineData(-5, 6, false)]
	[InlineData(30, 0, false)]
	[InlineData(30, 6, true)]
	[InlineData(null, 6, true)]
	public void IsValid_Always_RejectsNonPositiveValues(int? defaultDays, int intervalHours, bool expected)
	{
		var options = new LogStreamRetentionOptions { DefaultDays = defaultDays, IntervalHours = intervalHours };

		RetentionPolicy.IsValid(options).Should().Be(expected);
	}

	[Fact]
	public void IsValid_WithNonPositiveTenantOverride_ReturnsFalse()
	{
		var options = new LogStreamRetentionOptions { DefaultDays = 30 };
		options.DaysByTenant[Guid.NewGuid()] = 0;

		RetentionPolicy.IsValid(options).Should().BeFalse("config inválida jamais expurga (fail-safe)");
	}

	[Fact]
	public void ResolveAuditDays_WhenNotConfigured_ReturnsNull()
	{
		RetentionPolicy.ResolveAuditDays(new LogStreamRetentionOptions(), Guid.NewGuid())
			.Should().BeNull("sem configuração explícita, a trilha de auditoria nunca expira (default intencional)");
	}

	[Fact]
	public void ResolveAuditDays_WhenTenantOverridden_PrefersOverride()
	{
		var tenantId = Guid.NewGuid();
		var options = new LogStreamRetentionOptions { AuditDefaultDays = 365 };
		options.AuditDaysByTenant[tenantId] = 2555;

		RetentionPolicy.ResolveAuditDays(options, tenantId).Should().Be(2555);
		RetentionPolicy.ResolveAuditDays(options, Guid.NewGuid()).Should().Be(365);
	}

	[Fact]
	public void ResolveAuditDays_IsIndependentFromDiagnosticWindow()
	{
		var tenantId = Guid.NewGuid();
		var options = new LogStreamRetentionOptions { DefaultDays = 30 };

		RetentionPolicy.ResolveDays(options, tenantId).Should().Be(30);
		RetentionPolicy.ResolveAuditDays(options, tenantId).Should().BeNull(
			"a janela de diagnóstico configurada sozinha não implica janela de auditoria");
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(-1, false)]
	[InlineData(365, true)]
	[InlineData(null, true)]
	public void IsValid_WithAuditDefaultDays_RejectsNonPositiveValues(int? auditDefaultDays, bool expected)
	{
		var options = new LogStreamRetentionOptions { AuditDefaultDays = auditDefaultDays };

		RetentionPolicy.IsValid(options).Should().Be(expected);
	}

	[Fact]
	public void IsValid_WithNonPositiveAuditTenantOverride_ReturnsFalse()
	{
		var options = new LogStreamRetentionOptions { AuditDefaultDays = 365 };
		options.AuditDaysByTenant[Guid.NewGuid()] = 0;

		RetentionPolicy.IsValid(options).Should().BeFalse("config de auditoria inválida jamais expurga (fail-safe)");
	}
}
