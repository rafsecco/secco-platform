using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SecureGate.Application.InstallationOperators;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Infrastructure.TenantAdministration;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Factory dos testes de admin de tenant (ADR-0044): o SecureGate validando os próprios tokens, com o
/// auditor das escritas substituído por um controlável.
/// </summary>
public sealed class TenantAdminSecureGateApiFactory : SelfIssuedAuthSecureGateApiFactory
{
	/// <summary>Limite de tenants criados por pessoa nesta suíte — baixo, para o teste de limite ser curto (ADR-0047).</summary>
	public const int MaxCreatedTenantsPerPerson = 2;

	/// <summary>Auditor controlável, compartilhado pela coleção (os testes dela rodam em sequência).</summary>
	public FakeTenantAdminAuditor Auditor { get; } = new();

	/// <summary>
	/// Quando verdadeiro, a trilha local da gestão por máquina (ADR-0054) recusa o registro — simula falha de gravação
	/// no banco do SecureGate, para provar o 503 sem escrita.
	/// </summary>
	public bool FailTenantManageAudit { get; set; }

	/// <summary>Designações de operador controláveis (ADR-0049): vazias por padrão, como numa instalação sem designação.</summary>
	public FakeDesignations Designations { get; } = new();

	/// <summary>Relógio da janela de autenticação recente (ADR-0049), adiantável para simular autenticação antiga.</summary>
	public ShiftedClock DesignationClock { get; } = new();

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		base.ConfigureTestConfiguration(settings);

		ArgumentNullException.ThrowIfNull(settings);

		// Provisionamento automático ligado, contra o SQL Server da própria suíte (mesmo arranjo da issue #3):
		// o admin de tenant só provisiona assim (ADR-0047)
		var admin = new SqlConnectionStringBuilder(GetPlatformConnectionString()) { InitialCatalog = "master" };
		settings["SecureGate:Provisioning:Targets:default:Provider"] = "SqlServer";
		settings["SecureGate:Provisioning:Targets:default:Server"] = admin.DataSource;
		settings["SecureGate:Provisioning:Targets:default:AdminConnectionString"] = admin.ConnectionString;
		settings["SecureGate:TenantAdmin:MaxCreatedTenantsPerPerson"] =
			MaxCreatedTenantsPerPerson.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		base.ConfigureTestServices(services);

		ArgumentNullException.ThrowIfNull(services);

		services.RemoveAll<ITenantAdminAuditor>();
		services.AddSingleton<ITenantAdminAuditor>(Auditor);

		// Trilha local da gestão por máquina (ADR-0054): a real, embrulhada para o teste poder fazê-la falhar
		services.RemoveAll<ITenantManageAuditor>();
		services.AddScoped<ITenantManageAuditor>(serviceProvider => new FaultableTenantManageAuditor(
			ActivatorUtilities.CreateInstance<DatabaseTenantManageAuditor>(serviceProvider), () => FailTenantManageAudit));
		services.RemoveAll<IInstallationOperatorDesignations>();
		services.AddSingleton<IInstallationOperatorDesignations>(Designations);
		services.AddKeyedSingleton<TimeProvider>(InstallationOperatorOptions.ClockKey, DesignationClock);
	}
}

/// <summary>Auditor de teste: controla configuração e aceite, e guarda o que foi pedido para registrar.</summary>
public sealed class FakeTenantAdminAuditor : ITenantAdminAuditor
{
	private readonly List<TenantAdminAuditRecord> _records = [];
	private readonly Lock _gate = new();

	/// <inheritdoc />
	public bool IsConfigured { get; set; } = true;

	/// <summary>Resultado devolvido por <see cref="RecordAsync"/>.</summary>
	public bool Accept { get; set; } = true;

	/// <summary>Registros pedidos, na ordem.</summary>
	public IReadOnlyList<TenantAdminAuditRecord> Records
	{
		get
		{
			lock (_gate)
			{
				return [.. _records];
			}
		}
	}

	/// <summary>Volta ao estado padrão: configurado, aceitando, sem registros.</summary>
	public void Reset()
	{
		IsConfigured = true;
		Accept = true;

		lock (_gate)
		{
			_records.Clear();
		}
	}

	/// <inheritdoc />
	public Task<bool> RecordAsync(TenantAdminAuditRecord record, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			_records.Add(record);
		}

		return Task.FromResult(Accept);
	}
}

/// <summary>Trilha local real (ADR-0054) com uma chave de falha, para provar o fail-closed.</summary>
public sealed class FaultableTenantManageAuditor(ITenantManageAuditor inner, Func<bool> shouldFail) : ITenantManageAuditor
{
	/// <inheritdoc />
	public Task<bool> RecordAsync(TenantManageAuditRecord record, CancellationToken cancellationToken = default) =>
		shouldFail() ? Task.FromResult(false) : inner.RecordAsync(record, cancellationToken);
}

/// <summary>Designações de operador mutáveis pelo teste (ADR-0049).</summary>
public sealed class FakeDesignations : IInstallationOperatorDesignations
{
	private readonly HashSet<Guid> _accounts = [];
	private readonly Lock _gate = new();

	/// <inheritdoc />
	public IReadOnlyCollection<Guid> Current
	{
		get
		{
			lock (_gate)
			{
				return [.. _accounts];
			}
		}
	}

	/// <inheritdoc />
	public bool IsDesignated(Guid userId)
	{
		lock (_gate)
		{
			return _accounts.Contains(userId);
		}
	}

	/// <summary>Designa a conta.</summary>
	public void Designate(Guid userId)
	{
		lock (_gate)
		{
			_accounts.Add(userId);
		}
	}

	/// <summary>Tira a designação da conta.</summary>
	public void Revoke(Guid userId)
	{
		lock (_gate)
		{
			_accounts.Remove(userId);
		}
	}

	/// <summary>Volta ao estado sem designação.</summary>
	public void Reset()
	{
		lock (_gate)
		{
			_accounts.Clear();
		}
	}
}

/// <summary>Relógio do sistema com deslocamento controlável.</summary>
public sealed class ShiftedClock : TimeProvider
{
	/// <summary>Deslocamento aplicado ao relógio do sistema.</summary>
	public TimeSpan Offset { get; set; }

	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
}

/// <summary>Collection dos testes de admin de tenant: container e API próprios, auditor controlável.</summary>
[CollectionDefinition(Name)]
public sealed class TenantAdminApiCollectionDefinition : ICollectionFixture<TenantAdminSecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate admin de tenant";
}
