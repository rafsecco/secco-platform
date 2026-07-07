using FluentAssertions;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SharedKernel.Tests.Results;

public class ResultTests
{
    private static readonly Error SomeError = Error.Failure("Platform.Test.Failure", "Falha de teste.");

    [Fact]
    public void Success_Always_ReturnsSuccessWithErrorNone()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_Always_ReturnsFailureWithGivenError()
    {
        var result = Result.Failure(SomeError);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void SuccessOfT_Always_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void FailureOfT_Always_ReturnsFailureWithGivenError()
    {
        var result = Result.Failure<int>(SomeError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void Value_WhenFailure_ThrowsInvalidOperationException()
    {
        var result = Result.Failure<int>(SomeError);

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{SomeError.Code}*");
    }

    [Fact]
    public void ImplicitConversion_FromValue_CreatesSuccess()
    {
        Result<string> result = "ok";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public void ImplicitConversion_FromError_CreatesFailure()
    {
        Result<string> result = SomeError;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void SuccessOfT_WithNullValue_IsAllowedAndExposesNull()
    {
        var result = Result.Success<string?>(null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }
}
