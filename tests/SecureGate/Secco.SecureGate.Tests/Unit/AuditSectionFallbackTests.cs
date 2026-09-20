using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Secco.SecureGate.Infrastructure.Elevation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// A identidade de auditoria deixou de ser só da elevação (ADR-0031) e passou a registrar também
/// os eventos de credencial (ADR-0033), então a seção virou <c>SecureGate:Audit</c>. O nome antigo
/// continua valendo: uma instalação existente não pode perder a auditoria — e, com ela, a
/// elevação — por causa de um rename de chave.
/// </summary>
public class AuditSectionFallbackTests
{
	private static AuditOptionsBinder.Result Bind(Dictionary<string, string?> settings) =>
		AuditOptionsBinder.Bind(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

	private static Dictionary<string, string?> Complete(string section) => new()
	{
		[$"{section}:LogStreamBaseUrl"] = "https://logs.exemplo",
		[$"{section}:AuthorityUrl"] = "https://id.exemplo",
		[$"{section}:ClientId"] = "auditor",
		[$"{section}:ClientSecret"] = "segredo-de-teste",
	};

	[Fact]
	public void Secao_NomeNovo_EhLida()
	{
		var result = Bind(Complete("SecureGate:Audit"));

		result.Options.IsConfigured.Should().BeTrue();
		result.UsedLegacySection.Should().BeFalse();
	}

	[Fact]
	public void Secao_NomeAntigo_ContinuaValendo()
	{
		var result = Bind(Complete("SecureGate:ElevationAudit"));

		result.Options.IsConfigured.Should().BeTrue();
		result.Options.ClientId.Should().Be("auditor");
		result.UsedLegacySection.Should().BeTrue("o aviso de startup depende de saber que o nome antigo foi usado");
	}

	[Fact]
	public void Secao_ComOsDoisNomes_ONovoVence()
	{
		var settings = Complete("SecureGate:ElevationAudit");
		settings["SecureGate:Audit:LogStreamBaseUrl"] = "https://logs-novo.exemplo";
		settings["SecureGate:Audit:AuthorityUrl"] = "https://id.exemplo";
		settings["SecureGate:Audit:ClientId"] = "auditor-novo";
		settings["SecureGate:Audit:ClientSecret"] = "segredo-de-teste";

		var result = Bind(settings);

		result.Options.ClientId.Should().Be("auditor-novo");
		result.UsedLegacySection.Should().BeFalse();
	}

	[Fact]
	public void Secao_Ausente_ContinuaDesligada()
	{
		var result = Bind([]);

		result.Options.IsConfigured.Should().BeFalse();
		result.UsedLegacySection.Should().BeFalse();
	}

	[Fact]
	public void Secao_AntigaPelaMetade_SegueInvalida()
	{
		var result = Bind(new Dictionary<string, string?>
		{
			["SecureGate:ElevationAudit:LogStreamBaseUrl"] = "https://logs.exemplo",
		});

		result.Options.TryValidate(out var error).Should().BeFalse();
		error.Should().NotContain("segredo");
	}
}
