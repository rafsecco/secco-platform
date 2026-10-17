using FluentAssertions;
using Xunit;

namespace Secco.SDK.Imaging.Tests;

public sealed class RequestValidationTests
{
	public static TheoryData<ImageProcessingRequest> Invalid => new()
	{
		new ImageProcessingRequest([]),
		new ImageProcessingRequest([new ImageVariant("", 100)]),
		new ImageProcessingRequest([new ImageVariant("a", 100), new ImageVariant("a", 200)]),
		new ImageProcessingRequest([new ImageVariant("a", 0)]),
		new ImageProcessingRequest([new ImageVariant("a", 4097)]),
		new ImageProcessingRequest([new ImageVariant("a", 100)], Quality: 0),
		new ImageProcessingRequest([new ImageVariant("a", 100)], Quality: 101),
	};

	[Theory]
	[MemberData(nameof(Invalid))]
	public void Requisicao_invalida_lanca(ImageProcessingRequest request)
	{
		var act = () => ImageProcessingRequestValidation.ThrowIfInvalid(request);

		act.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData(1)]
	[InlineData(100)]
	public void Requisicao_no_limite_nao_lanca(int quality)
	{
		var request = new ImageProcessingRequest([new ImageVariant("a", 4096)], Quality: quality);

		var act = () => ImageProcessingRequestValidation.ThrowIfInvalid(request);

		act.Should().NotThrow();
	}
}
