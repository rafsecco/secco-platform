using FluentAssertions;
using Microsoft.Data.SqlClient;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Provisioning;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Provisioning;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Geração de segredo, montagem do script e as recusas do handler.</summary>
public class TenantDatabaseProvisioningTests
{
	private static readonly TenantDatabaseProvisioningPlan Plan = new(
		"sql-01.interno,1433",
		"secco_logstream_contoso",
		"secco_logstream_contoso_app",
		"SenhaDeTeste123456789012345678901234",
		CreateDatabase: true);

	private static ProvisionTenantDatabaseHandler CreateHandler(
		ITenantRepository repository,
		TenantDatabaseProvisioningOptions? options = null) =>
		new(repository, options ?? new TenantDatabaseProvisioningOptions(),
			[new SqlServerTenantDatabaseProvisioner()]);

	private static ITenantRepository RepositoryWithTenant(TenantDatabase? existingDatabase = null)
	{
		var repository = Substitute.For<ITenantRepository>();
		repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new Tenant("Contoso", "contoso"));
		repository.GetDatabaseAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(existingDatabase);

		return repository;
	}

	[Fact]
	public void Password_IsGeneratedWithSafeAlphabetAndMinimumLength()
	{
		var password = TenantDatabaseSecretGenerator.GeneratePassword();

		password.Should().HaveLength(TenantDatabaseSecretGenerator.PasswordLength);

		// Estes caracteres quebrariam connection string ou literal SQL — o alfabeto os exclui.
		password.Should().NotContainAny(";", "'", "\"", "=");
	}

	[Fact]
	public void Password_IsDifferentOnEachCall() =>
		TenantDatabaseSecretGenerator.GeneratePassword()
			.Should().NotBe(TenantDatabaseSecretGenerator.GeneratePassword());

	[Fact]
	public void Script_WhenCreateDatabaseIsTrue_CreatesDatabaseLoginUserAndGrantsOnlyThatDatabase()
	{
		var script = new SqlServerTenantDatabaseProvisioner().BuildScript(Plan);

		script.Should().Contain("CREATE DATABASE [secco_logstream_contoso]");
		script.Should().Contain("CREATE LOGIN [secco_logstream_contoso_app]");
		script.Should().Contain("CREATE USER [secco_logstream_contoso_app]");
		script.Should().Contain("ALTER ROLE db_owner ADD MEMBER [secco_logstream_contoso_app]");

		// O teto do que se concede: nada no servidor.
		script.Should().NotContain("sysadmin");
		script.Should().NotContain("dbcreator");
		script.Should().NotContain("securityadmin");
		script.Should().NotContain("ALTER SERVER ROLE");
	}

	[Fact]
	public void Script_WhenCreateDatabaseIsFalse_OmitsCreateDatabase()
	{
		var script = new SqlServerTenantDatabaseProvisioner()
			.BuildScript(Plan with { CreateDatabase = false });

		script.Should().NotContain("CREATE DATABASE");
		script.Should().Contain("CREATE LOGIN");
	}

	[Fact]
	public void TenantConnectionString_UsesTheCreatedUserAndItsOwnDatabase()
	{
		var connectionString = new SqlServerTenantDatabaseProvisioner().BuildTenantConnectionString(Plan);
		var builder = new SqlConnectionStringBuilder(connectionString);

		builder.InitialCatalog.Should().Be("secco_logstream_contoso");
		builder.UserID.Should().Be("secco_logstream_contoso_app");
		builder.UserID.Should().NotBe("sa", "o ponto da entrega é justamente deixar de usar SA");
	}

	[Fact]
	public async Task Provision_WhenAutomationNotConfigured_ReturnsScriptAndAppliedFalse()
	{
		var result = await CreateHandler(RepositoryWithTenant()).HandleAsync(
			new ProvisionTenantDatabaseCommand(
				Guid.CreateVersion7(), "logstream", null, "sql-01.interno", true, null, null));

		result.IsSuccess.Should().BeTrue();
		result.Value.Applied.Should().BeFalse();
		result.Value.Script.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public async Task Provision_WhenAlreadyProvisioned_ReturnsConflict()
	{
		var tenantId = Guid.CreateVersion7();
		var repository = RepositoryWithTenant(
			new TenantDatabase(tenantId, "logstream", "Server=x;Database=y;User Id=z;Password=w;"));

		var result = await CreateHandler(repository).HandleAsync(
			new ProvisionTenantDatabaseCommand(tenantId, "logstream", null, "sql-01", true, null, null));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.TenantDatabases.AlreadyProvisioned);
	}

	[Fact]
	public async Task Provision_WhenNoServerAndNoTarget_Fails()
	{
		var result = await CreateHandler(RepositoryWithTenant()).HandleAsync(
			new ProvisionTenantDatabaseCommand(
				Guid.CreateVersion7(), "logstream", null, null, true, null, null));

		result.Error.Should().Be(SecureGateErrors.TenantDatabases.ProvisioningServerRequired);
	}

	[Fact]
	public async Task Provision_WhenTargetIsUnknown_Fails()
	{
		var result = await CreateHandler(RepositoryWithTenant()).HandleAsync(
			new ProvisionTenantDatabaseCommand(
				Guid.CreateVersion7(), "logstream", "inexistente", "sql-01", true, null, null));

		result.Error.Should().Be(SecureGateErrors.TenantDatabases.ProvisioningTargetUnknown);
	}

	[Fact]
	public async Task Provision_WhenExplicitNameEscapesTheAllowlist_Fails()
	{
		var result = await CreateHandler(RepositoryWithTenant()).HandleAsync(
			new ProvisionTenantDatabaseCommand(
				Guid.CreateVersion7(), "logstream", null, "sql-01", true,
				"secco;DROP DATABASE master--", null));

		result.Error.Should().Be(SecureGateErrors.TenantDatabases.ProvisioningIdentifierInvalid);
	}

	[Fact]
	public void Options_WhenTargetIsDeclaredWithoutServer_FailsValidation()
	{
		var options = new TenantDatabaseProvisioningOptions();
		options.Targets["default"] = new ProvisioningTarget { Provider = "SqlServer" };

		options.TryValidate(out var error).Should().BeFalse();
		error.Should().Contain("Server");
	}

	[Fact]
	public void Options_WhenSectionIsAbsent_IsValidAndAutomationIsOff()
	{
		var options = new TenantDatabaseProvisioningOptions();

		options.TryValidate(out _).Should().BeTrue();
		options.Resolve(null).Should().BeNull("seção ausente desliga a automação, não o recurso");
	}

	[Fact]
	public void Target_WithoutAdminConnectionString_CannotExecute() =>
		new ProvisioningTarget { Provider = "SqlServer", Server = "sql-01" }
			.CanExecute.Should().BeFalse();
}
