using FluentAssertions;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SharedKernel.Tests.Results;

public class ValidationErrorTests
{
    private static readonly Error NameRequired = Error.Validation("User.Name.Required", "Nome é obrigatório.");
    private static readonly Error EmailInvalid = Error.Validation("User.Email.Invalid", "E-mail inválido.");

    [Fact]
    public void Constructor_WithMultipleErrors_AggregatesAllOfThem()
    {
        var validationError = new ValidationError(NameRequired, EmailInvalid);

        validationError.Errors.Should().HaveCount(2);
        validationError.Errors.Should().ContainInOrder(NameRequired, EmailInvalid);
    }

    [Fact]
    public void Constructor_Always_UsesAggregateCodeAndValidationType()
    {
        var validationError = new ValidationError(NameRequired);

        validationError.Code.Should().Be(ValidationError.AggregateCode);
        validationError.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Constructor_WhenNoErrors_ThrowsArgumentException()
    {
        var act = () => new ValidationError();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failure_WithValidationError_IsAssignableToError()
    {
        var result = Result.Failure<string>(new ValidationError(NameRequired));

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainSingle();
    }
}
