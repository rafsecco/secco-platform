using FluentAssertions;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SharedKernel.Tests.Results;

public class ErrorTests
{
    [Fact]
    public void None_Always_HasEmptyCodeAndTypeNone()
    {
        Error.None.Code.Should().BeEmpty();
        Error.None.Description.Should().BeEmpty();
        Error.None.Type.Should().Be(ErrorType.None);
    }

    [Fact]
    public void Validation_Always_CreatesErrorWithValidationType()
    {
        var error = Error.Validation("Platform.Field.Invalid", "Campo inválido.");

        error.Code.Should().Be("Platform.Field.Invalid");
        error.Description.Should().Be("Campo inválido.");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void NotFound_Always_CreatesErrorWithNotFoundType()
    {
        var error = Error.NotFound("Platform.Tenant.NotFound", "Tenant não encontrado.");

        error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Conflict_Always_CreatesErrorWithConflictType()
    {
        var error = Error.Conflict("Platform.User.Duplicated", "Usuário já existe.");

        error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void Unauthorized_Always_CreatesErrorWithUnauthorizedType()
    {
        var error = Error.Unauthorized("Platform.Token.Missing", "Token ausente.");

        error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public void Forbidden_Always_CreatesErrorWithForbiddenType()
    {
        var error = Error.Forbidden("Platform.Scope.Missing", "Escopo insuficiente.");

        error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public void Failure_Always_CreatesErrorWithFailureType()
    {
        var error = Error.Failure("Platform.General.Failure", "Falha de negócio.");

        error.Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public void Equals_WhenSameCodeDescriptionAndType_ReturnsTrue()
    {
        var left = Error.NotFound("Platform.Tenant.NotFound", "Tenant não encontrado.");
        var right = Error.NotFound("Platform.Tenant.NotFound", "Tenant não encontrado.");

        left.Should().Be(right);
    }

    [Fact]
    public void Equals_WhenDifferentCode_ReturnsFalse()
    {
        var left = Error.NotFound("Platform.Tenant.NotFound", "Tenant não encontrado.");
        var right = Error.NotFound("Platform.User.NotFound", "Tenant não encontrado.");

        left.Should().NotBe(right);
    }
}
