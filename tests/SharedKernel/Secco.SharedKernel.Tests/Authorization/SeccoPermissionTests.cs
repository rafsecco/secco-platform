using FluentAssertions;
using Secco.SharedKernel.Authorization;
using Xunit;

namespace Secco.SharedKernel.Tests.Authorization;

public class SeccoPermissionTests
{
    [Theory]
    [InlineData("log-entries:read")]
    [InlineData("invoices:write")]
    [InlineData("api-call-logs:read")]
    [InlineData("a:b")]
    [InlineData("recurso1:acao-composta-2")]
    public void IsValid_WithCanonicalFormat_ReturnsTrue(string permission) =>
        SeccoPermissions.IsValid(permission).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("semseparador")]
    [InlineData("recurso:")]
    [InlineData(":acao")]
    [InlineData("Recurso:Acao")]
    [InlineData("recurso:acao:extra")]
    [InlineData("recurso :acao")]
    [InlineData("recurso:ação")]
    [InlineData("-recurso:acao")]
    [InlineData("recurso-:acao")]
    public void IsValid_WithInvalidFormat_ReturnsFalse(string? permission) =>
        SeccoPermissions.IsValid(permission).Should().BeFalse();

    [Fact]
    public void IsValid_AboveMaxLength_ReturnsFalse()
    {
        var permission = $"{new string('a', SeccoPermissions.MaxLength)}:read";

        SeccoPermissions.IsValid(permission).Should().BeFalse();
    }

    [Fact]
    public void Create_WithValidParts_ComposesPermission() =>
        SeccoPermissions.Create("log-entries", "read").Should().Be("log-entries:read");

    [Theory]
    [InlineData("Recurso", "read")]
    [InlineData("recurso", "")]
    [InlineData("recurso com espaco", "read")]
    public void Create_WithInvalidParts_Throws(string resource, string action)
    {
        var act = () => SeccoPermissions.Create(resource, action);

        act.Should().Throw<ArgumentException>();
    }
}
