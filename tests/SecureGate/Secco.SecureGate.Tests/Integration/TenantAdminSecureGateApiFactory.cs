using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SecureGate.Application.TenantAdministration;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Factory dos testes de admin de tenant (ADR-0044): o SecureGate validando os próprios tokens, com o
/// auditor das escritas substituído por um controlável.
/// </summary>
public sealed class TenantAdminSecureGateApiFactory : SelfIssuedAuthSecureGateApiFactory
{
	/// <summary>Auditor controlável, compartilhado pela coleção (os testes dela rodam em sequência).</summary>
	public FakeTenantAdminAuditor Auditor { get; } = new();

	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		base.ConfigureTestServices(services);

		ArgumentNullException.ThrowIfNull(services);

		services.RemoveAll<ITenantAdminAuditor>();
		services.AddSingleton<ITenantAdminAuditor>(Auditor);
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

/// <summary>Collection dos testes de admin de tenant: container e API próprios, auditor controlável.</summary>
[CollectionDefinition(Name)]
public sealed class TenantAdminApiCollectionDefinition : ICollectionFixture<TenantAdminSecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate admin de tenant";
}
