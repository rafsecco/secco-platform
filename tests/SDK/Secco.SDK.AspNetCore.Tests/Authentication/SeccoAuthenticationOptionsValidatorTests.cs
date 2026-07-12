using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Authentication;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authentication;

public class SeccoAuthenticationOptionsValidatorTests
{
    private const string ValidKey = "chave-de-testes-com-32-caracteres!!";

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Secco.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static SeccoAuthenticationOptionsValidator CreateValidator(string environment = "Development") =>
        new(new FakeHostEnvironment(environment));

    [Fact]
    public void Validate_WithDevelopmentKeyOutsideProduction_Succeeds()
    {
        var result = CreateValidator().Validate(null, new SeccoAuthenticationOptions
        {
            Audience = "secco-tests",
            Issuer = "secco-tests",
            DevelopmentSigningKey = ValidKey,
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithAuthorityInProduction_Succeeds()
    {
        var result = CreateValidator("Production").Validate(null, new SeccoAuthenticationOptions
        {
            Audience = "secco-tests",
            Authority = "https://securegate.example.com",
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithDevelopmentKeyInProduction_Fails()
    {
        var result = CreateValidator("Production").Validate(null, new SeccoAuthenticationOptions
        {
            Audience = "secco-tests",
            Issuer = "secco-tests",
            DevelopmentSigningKey = ValidKey,
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Production");
    }

    [Fact]
    public void Validate_WithoutAuthorityNorKey_Fails()
    {
        var result = CreateValidator().Validate(null, new SeccoAuthenticationOptions { Audience = "secco-tests" });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithBothAuthorityAndKey_FailsAsAmbiguous()
    {
        var result = CreateValidator().Validate(null, new SeccoAuthenticationOptions
        {
            Audience = "secco-tests",
            Issuer = "secco-tests",
            Authority = "https://securegate.example.com",
            DevelopmentSigningKey = ValidKey,
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("mutuamente exclusivos");
    }

    [Fact]
    public void Validate_WithoutAudience_Fails()
    {
        var result = CreateValidator().Validate(null, new SeccoAuthenticationOptions
        {
            Issuer = "secco-tests",
            DevelopmentSigningKey = ValidKey,
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Audience");
    }

    [Fact]
    public void Validate_WithShortKey_Fails()
    {
        var result = CreateValidator().Validate(null, new SeccoAuthenticationOptions
        {
            Audience = "secco-tests",
            Issuer = "secco-tests",
            DevelopmentSigningKey = "curta",
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("32");
    }

    [Fact]
    public void Validate_WithKeyButNoIssuer_Fails()
    {
        var result = CreateValidator().Validate(null, new SeccoAuthenticationOptions
        {
            Audience = "secco-tests",
            DevelopmentSigningKey = ValidKey,
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Issuer");
    }
}
