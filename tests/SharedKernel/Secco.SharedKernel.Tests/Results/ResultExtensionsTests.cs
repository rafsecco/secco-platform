using System.Globalization;
using FluentAssertions;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SharedKernel.Tests.Results;

public class ResultExtensionsTests
{
    private static readonly Error SomeError = Error.Failure("Platform.Test.Failure", "Falha de teste.");

    [Fact]
    public void Match_WhenSuccess_InvokesOnSuccessBranch()
    {
        var result = Result.Success();

        var outcome = result.Match(() => "sucesso", error => $"falha: {error.Code}");

        outcome.Should().Be("sucesso");
    }

    [Fact]
    public void Match_WhenFailure_InvokesOnFailureBranchWithError()
    {
        var result = Result.Failure(SomeError);

        var outcome = result.Match(() => "sucesso", error => $"falha: {error.Code}");

        outcome.Should().Be($"falha: {SomeError.Code}");
    }

    [Fact]
    public void MatchOfT_WhenSuccess_InvokesOnSuccessBranchWithValue()
    {
        var result = Result.Success(10);

        var outcome = result.Match(value => value * 2, _ => 0);

        outcome.Should().Be(20);
    }

    [Fact]
    public void MatchOfT_WhenFailure_InvokesOnFailureBranchWithError()
    {
        var result = Result.Failure<int>(SomeError);

        var outcome = result.Match(value => value * 2, error => -1);

        outcome.Should().Be(-1);
    }

    [Fact]
    public void Map_WhenSuccess_TransformsValue()
    {
        var result = Result.Success(21);

        var mapped = result.Map(value => value * 2);

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(42);
    }

    [Fact]
    public void Map_WhenFailure_PropagatesErrorWithoutInvokingMap()
    {
        var invoked = false;
        var result = Result.Failure<int>(SomeError);

        var mapped = result.Map(value =>
        {
            invoked = true;
            return value * 2;
        });

        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(SomeError);
        invoked.Should().BeFalse();
    }

    [Fact]
    public void Bind_WhenSuccess_ChainsNextOperation()
    {
        var result = Result.Success(5);

        var bound = result.Bind(value => Result.Success(value.ToString(CultureInfo.InvariantCulture)));

        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("5");
    }

    [Fact]
    public void Bind_WhenNextOperationFails_ReturnsItsError()
    {
        var result = Result.Success(5);

        var bound = result.Bind(_ => Result.Failure<string>(SomeError));

        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(SomeError);
    }

    [Fact]
    public void Bind_WhenFailure_PropagatesErrorWithoutInvokingBind()
    {
        var invoked = false;
        var result = Result.Failure<int>(SomeError);

        var bound = result.Bind(value =>
        {
            invoked = true;
            return Result.Success(value.ToString(CultureInfo.InvariantCulture));
        });

        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(SomeError);
        invoked.Should().BeFalse();
    }

    [Fact]
    public void BindToNonGeneric_WhenSuccess_ChainsNextOperation()
    {
        var result = Result.Success(5);

        var bound = result.Bind(_ => Result.Success());

        bound.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BindAsync_WhenSuccess_ChainsAsyncOperation()
    {
        var result = Result.Success(5);

        var bound = await result.BindAsync(value => Task.FromResult(Result.Success(value * 2)));

        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be(10);
    }

    [Fact]
    public async Task BindAsync_WhenFailure_PropagatesErrorWithoutInvokingBind()
    {
        var result = Result.Failure<int>(SomeError);

        var bound = await result.BindAsync(value => Task.FromResult(Result.Success(value * 2)));

        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(SomeError);
    }

    [Fact]
    public async Task MapAsync_OnTaskOfResult_TransformsValue()
    {
        var resultTask = Task.FromResult(Result.Success(21));

        var mapped = await resultTask.MapAsync(value => value * 2);

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(42);
    }

    [Fact]
    public async Task BindAsync_OnTaskOfResult_ChainsAsyncOperation()
    {
        var resultTask = Task.FromResult(Result.Success(5));

        var bound = await resultTask.BindAsync(value => Task.FromResult(Result.Success(value.ToString(CultureInfo.InvariantCulture))));

        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("5");
    }

    [Fact]
    public async Task MatchAsync_OnTaskOfResult_WhenFailure_InvokesOnFailureBranch()
    {
        var resultTask = Task.FromResult(Result.Failure<int>(SomeError));

        var outcome = await resultTask.MatchAsync(value => value * 2, _ => -1);

        outcome.Should().Be(-1);
    }

    [Fact]
    public void Pipeline_BindAndMapCombined_ShortCircuitsOnFirstFailure()
    {
        var outcome = Result.Success(10)
            .Bind(value => value > 100
                ? Result.Success(value)
                : Result.Failure<int>(SomeError))
            .Map(value => value * 2);

        outcome.IsFailure.Should().BeTrue();
        outcome.Error.Should().Be(SomeError);
    }
}
