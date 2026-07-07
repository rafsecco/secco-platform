using FluentAssertions;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.SharedKernel.Tests.Exceptions;

public class SeccoExceptionTests
{
    [Fact]
    public void DomainInvariantException_Always_IsASeccoException()
    {
        var exception = new DomainInvariantException("invariante violada");

        exception.Should().BeAssignableTo<SeccoException>();
        exception.Message.Should().Be("invariante violada");
    }

    [Fact]
    public void DomainInvariantException_WithInnerException_PreservesIt()
    {
        var inner = new InvalidOperationException("causa raiz");

        var exception = new DomainInvariantException("invariante violada", inner);

        exception.InnerException.Should().BeSameAs(inner);
    }
}
