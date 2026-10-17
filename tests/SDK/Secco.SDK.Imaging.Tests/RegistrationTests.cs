using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Secco.SDK.Imaging.Tests;

public sealed class RegistrationTests
{
	private const string Section = "Test:Imaging";

	private static ServiceProvider Build(Dictionary<string, string?>? values = null)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		services.AddSeccoImaging(Section);
		return services.BuildServiceProvider();
	}

	[Fact]
	public void AddSeccoImaging_resolve_processador_singleton()
	{
		using var provider = Build();

		var first = provider.GetRequiredService<ISeccoImageProcessor>();
		var second = provider.GetRequiredService<ISeccoImageProcessor>();

		first.Should().BeSameAs(second);
	}

	[Fact]
	public void Opcoes_padrao()
	{
		using var provider = Build();

		var options = provider.GetRequiredService<IOptions<SeccoImagingOptions>>().Value;

		options.MaxInputBytes.Should().Be(10L * 1024 * 1024);
		options.MaxPixels.Should().Be(50_000_000);
		options.MaxConcurrentOperations.Should().Be(Environment.ProcessorCount);
	}

	[Theory]
	[InlineData("MaxInputBytes")]
	[InlineData("MaxPixels")]
	[InlineData("MaxConcurrentOperations")]
	public void Opcoes_invalidas_falham_na_validacao(string key)
	{
		using var provider = Build(new() { [$"{Section}:{key}"] = "0" });

		var act = () => provider.GetRequiredService<IOptions<SeccoImagingOptions>>().Value;

		act.Should().Throw<OptionsValidationException>();
	}
}
