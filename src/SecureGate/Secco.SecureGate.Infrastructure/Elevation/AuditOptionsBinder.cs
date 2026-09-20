using Microsoft.Extensions.Configuration;

namespace Secco.SecureGate.Infrastructure.Elevation;

/// <summary>
/// Lê a identidade de auditoria da seção <c>SecureGate:Audit</c> e, quando ela está ausente,
/// da antiga <c>SecureGate:ElevationAudit</c>.
/// </summary>
/// <remarks>
/// A identidade deixou de ser só da elevação (ADR-0031): com a ADR-0033 ela também registra os
/// eventos de credencial. O nome antigo segue aceito porque uma instalação em produção não pode
/// perder a auditoria — e, junto com ela, a capacidade de elevação — por causa de um rename de
/// chave; quem o usa recebe um aviso no startup apontando o nome novo.
/// </remarks>
internal static class AuditOptionsBinder
{
	/// <summary>Resultado do bind: as opções e de qual seção elas vieram.</summary>
	/// <param name="Options">Opções lidas (não configuradas quando nenhuma seção existe).</param>
	/// <param name="UsedLegacySection">Verdadeiro quando os valores vieram do nome antigo.</param>
	internal sealed record Result(ElevationAuditOptions Options, bool UsedLegacySection);

	/// <summary>Faz o bind com precedência do nome novo.</summary>
	/// <param name="configuration">Configuração da aplicação.</param>
	internal static Result Bind(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var options = new ElevationAuditOptions();
		configuration.GetSection(ElevationAuditOptions.SectionKey).Bind(options);

		if (options.IsConfigured)
		{
			return new Result(options, UsedLegacySection: false);
		}

		var legacy = new ElevationAuditOptions();
		configuration.GetSection(ElevationAuditOptions.LegacySectionKey).Bind(legacy);

		return new Result(legacy, legacy.IsConfigured);
	}
}
