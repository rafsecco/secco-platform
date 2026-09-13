using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SecureGate.Application.Elevation;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Factory dos testes de elevação (ADR-0031): o SecureGate validando os próprios tokens, com o
/// auditor substituído por um controlável.
/// </summary>
/// <remarks>
/// Configura o TTL da elevação ACIMA do teto de propósito. Com isso todo token elevado emitido
/// nestes testes prova o teto de passagem — se o limite falhar, o token sai com dez horas e o
/// teste de emissão pega.
/// </remarks>
public sealed class ElevationSecureGateApiFactory : SelfIssuedAuthSecureGateApiFactory
{
	/// <summary>TTL pedido na configuração, muito acima do teto de 60 minutos.</summary>
	public const int ConfiguredLifetimeMinutes = 600;

	/// <summary>Auditor controlável, compartilhado pela coleção (os testes dela rodam em sequência).</summary>
	public FakeElevationAuditor Auditor { get; } = new();

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		base.ConfigureTestConfiguration(settings);

		ArgumentNullException.ThrowIfNull(settings);

		settings["SecureGate:Elevation:TokenLifetimeMinutes"] = ConfiguredLifetimeMinutes.ToString(
			System.Globalization.CultureInfo.InvariantCulture);
	}

	/// <inheritdoc />
	protected override void ConfigureTestServices(IServiceCollection services)
	{
		base.ConfigureTestServices(services);

		ArgumentNullException.ThrowIfNull(services);

		services.RemoveAll<IElevationAuditor>();
		services.AddSingleton<IElevationAuditor>(Auditor);
	}
}

/// <summary>
/// Auditor de teste: controla se há identidade de auditoria configurada e se o registro é aceito,
/// e guarda o que foi pedido para registrar.
/// </summary>
public sealed class FakeElevationAuditor : IElevationAuditor
{
	private readonly List<ElevationAuditRecord> _records = [];
	private readonly Lock _gate = new();

	/// <inheritdoc />
	public bool IsConfigured { get; set; } = true;

	/// <summary>Resultado devolvido por <see cref="RecordAsync"/>.</summary>
	public bool Accept { get; set; } = true;

	/// <summary>Registros pedidos, na ordem.</summary>
	public IReadOnlyList<ElevationAuditRecord> Records
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
	public Task<bool> RecordAsync(ElevationAuditRecord record, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			_records.Add(record);
		}

		return Task.FromResult(Accept);
	}
}

/// <summary>Collection dos testes de elevação: container e API próprios, auditor controlável.</summary>
[CollectionDefinition(Name)]
public sealed class ElevationApiCollectionDefinition : ICollectionFixture<ElevationSecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate elevação";
}
