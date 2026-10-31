using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Domain.Scheduling;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Opções do ciclo (ADR-0055): padrões, faixas válidas e a regra "fora da faixa cai no padrão".</summary>
public class DirectorySyncOptionsTests
{
	[Fact]
	public void Padroes_SaoVinteEQuatroHoras_DezPorCento_ECincoRetiradas()
	{
		var options = new DirectorySyncOptions();

		options.EffectiveInterval.Should().Be(TimeSpan.FromHours(24));
		options.EffectiveMaxRemovalRatio.Should().Be(0.10);
		options.EffectiveMinRemovalsBeforeBrake.Should().Be(5);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(24)]
	[InlineData(168)]
	public void Intervalo_DentroDaFaixa_EUsado(int hours)
	{
		var options = new DirectorySyncOptions { IntervalHours = hours };

		options.IsIntervalInvalid.Should().BeFalse();
		options.EffectiveInterval.Should().Be(TimeSpan.FromHours(hours));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	[InlineData(169)]
	public void Intervalo_ForaDaFaixa_CaiNoPadraoEFicaMarcadoComoInvalido(int hours)
	{
		var options = new DirectorySyncOptions { IntervalHours = hours };

		options.IsIntervalInvalid.Should().BeTrue();
		options.EffectiveInterval.Should().Be(TimeSpan.FromHours(24));
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(0.5)]
	[InlineData(1.0)]
	public void Fracao_DentroDaFaixa_EUsada(double ratio)
	{
		var options = new DirectorySyncOptions { MaxRemovalRatio = ratio };

		options.IsRatioInvalid.Should().BeFalse();
		options.EffectiveMaxRemovalRatio.Should().Be(ratio);
	}

	[Theory]
	[InlineData(-0.1)]
	[InlineData(1.1)]
	[InlineData(double.NaN)]
	public void Fracao_ForaDaFaixa_CaiNoPadrao(double ratio)
	{
		var options = new DirectorySyncOptions { MaxRemovalRatio = ratio };

		options.IsRatioInvalid.Should().BeTrue();
		options.EffectiveMaxRemovalRatio.Should().Be(0.10);
	}

	[Fact]
	public void Piso_ZeroEValido_NegativoCaiNoPadrao()
	{
		new DirectorySyncOptions { MinRemovalsBeforeBrake = 0 }.EffectiveMinRemovalsBeforeBrake.Should().Be(0);

		var negative = new DirectorySyncOptions { MinRemovalsBeforeBrake = -1 };
		negative.IsMinRemovalsInvalid.Should().BeTrue();
		negative.EffectiveMinRemovalsBeforeBrake.Should().Be(5);
	}

	[Fact]
	public void BindConfiguration_LeAsTresChavesDaSecaoSecureGateDirectorySync()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["SecureGate:DirectorySync:IntervalHours"] = "6",
			["SecureGate:DirectorySync:MaxRemovalRatio"] = "0.25",
			["SecureGate:DirectorySync:MinRemovalsBeforeBrake"] = "2",
		}).Build();

		using var provider = new ServiceCollection()
			.AddSingleton<IConfiguration>(configuration)
			.AddOptions<DirectorySyncOptions>().BindConfiguration(DirectorySyncOptions.SectionKey).Services
			.BuildServiceProvider();

		var options = provider.GetRequiredService<IOptions<DirectorySyncOptions>>().Value;

		options.EffectiveInterval.Should().Be(TimeSpan.FromHours(6));
		options.EffectiveMaxRemovalRatio.Should().Be(0.25);
		options.EffectiveMinRemovalsBeforeBrake.Should().Be(2);
	}
}

/// <summary>Chave do vínculo com o diretório (ADR-0026) lida e escrita num lugar só.</summary>
public class DirectoryLinkKeyTests
{
	private static readonly Guid Directory = Guid.NewGuid();
	private static readonly Guid Object = Guid.NewGuid();

	[Fact]
	public void Build_EGetObjectId_SaoInversos()
	{
		DirectoryLinkKey.TryGetObjectId(DirectoryLinkKey.Build(Directory, Object), Directory, out var oid).Should().BeTrue();
		oid.Should().Be(Object);
	}

	[Fact]
	public void TryGetObjectId_ChaveDeOutroDiretorio_Recusa()
	{
		DirectoryLinkKey.TryGetObjectId(DirectoryLinkKey.Build(Guid.NewGuid(), Object), Directory, out _).Should().BeFalse();
	}

	[Theory]
	[InlineData("")]
	[InlineData("lixo")]
	[InlineData("nao-e-guid")]
	public void TryGetObjectId_ChaveMalformada_Recusa(string suffix)
	{
		DirectoryLinkKey.TryGetObjectId(DirectoryLinkKey.Prefix(Directory) + suffix, Directory, out _).Should().BeFalse();
	}

	[Fact]
	public void Prefix_SoTemHexadecimalEHifenEDoisPontos_SemCuringaDeLike()
	{
		DirectoryLinkKey.Prefix(Directory).Should().MatchRegex("^[0-9a-f-]+:$");
	}
}

/// <summary>Invariantes do lease de tarefa periódica (ADR-0055).</summary>
public class BackgroundLeaseTests
{
	[Fact]
	public void Cria_ComNomeDonoEExpiracao()
	{
		var expires = DateTimeOffset.UtcNow.AddMinutes(30);

		var lease = new BackgroundLease("directory-sync", "inst-1", expires);

		lease.Name.Should().Be("directory-sync");
		lease.Owner.Should().Be("inst-1");
		lease.ExpiresAt.Should().Be(expires);
	}

	[Theory]
	[InlineData("", "dono")]
	[InlineData("tarefa", " ")]
	public void Cria_SemNomeOuSemDono_Recusa(string name, string owner)
	{
		var act = () => new BackgroundLease(name, owner, DateTimeOffset.UtcNow);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Cria_ComNomeAcimaDoLimite_Recusa()
	{
		var act = () => new BackgroundLease(new string('x', BackgroundLease.NameMaxLength + 1), "dono", DateTimeOffset.UtcNow);

		act.Should().Throw<DomainInvariantException>();
	}
}
