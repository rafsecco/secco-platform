using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SecureGate.Tests.Integration.Helpers;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Decisão fail-closed do login federado (ADR-0026), provada SEM Entra real: o
/// <see cref="EntraSignInProcessor"/> é resolvido do DI e recebe principals fake com os
/// claims crus (<c>tid</c>/<c>oid</c>/<c>email</c>) que o esquema OIDC entregaria. Toda
/// recusa devolve o MESMO erro genérico (ADR-0020).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class EntraSignInProcessorTests(SecureGateApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ValidPassword = "Str0ng@Pass!";

    public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken([SecureGateScopes.Admin]));

        return client;
    }

    private static string UniqueEmail() => $"fed-{Guid.NewGuid():N}@secco.test";

    private async Task<Guid> CreateTenantAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant federado", slug = $"t-{Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    private async Task CreateUserAsync(HttpClient admin, Guid tenantId, string email)
    {
        var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
            new { email, password = ValidPassword });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task UpsertFederationAsync(HttpClient admin, Guid tenantId, Guid directoryId, bool enabled = true)
    {
        var response = await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
            new { directoryId, enabled });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static ClaimsPrincipal ExternalPrincipal(Guid? tid, Guid? oid, string? email)
    {
        var identity = new ClaimsIdentity(EntraSignInProcessor.LoginProvider);

        if (tid is { } t)
        {
            identity.AddClaim(new Claim("tid", t.ToString("D")));
        }

        if (oid is { } o)
        {
            identity.AddClaim(new Claim("oid", o.ToString("D")));
        }

        if (email is not null)
        {
            identity.AddClaim(new Claim("email", email));
        }

        return new ClaimsPrincipal(identity);
    }

    private async Task<TResult> WithScopeAsync<TResult>(
        Func<IServiceProvider, Task<TResult>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    [Fact]
    public async Task ProcessAsync_FirstLogin_SucceedsAndLinksExternalLogin()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        var directoryId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var email = UniqueEmail();
        await CreateUserAsync(admin, tenantId, email);
        await UpsertFederationAsync(admin, tenantId, directoryId);

        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(directoryId, objectId, email)));

        result.IsSuccess.Should().BeTrue("usuário provisionado + federação habilitada + diretório casando");
        result.Value.Email.Should().Be(email);

        var linked = await WithScopeAsync(services =>
            services.GetRequiredService<UserManager<User>>()
                .FindByLoginAsync(EntraSignInProcessor.LoginProvider, $"{directoryId:D}:{objectId:D}"));

        linked.Should().NotBeNull("o primeiro login federado vincula o oid em tb_user_logins (ADR-0026)");
    }

    [Fact]
    public async Task ProcessAsync_SecondLoginWithChangedEmail_StillMatchesByLink()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        var directoryId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var email = UniqueEmail();
        await CreateUserAsync(admin, tenantId, email);
        await UpsertFederationAsync(admin, tenantId, directoryId);

        (await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(directoryId, objectId, email))))
            .IsSuccess.Should().BeTrue();

        // E-mail mudou no diretório do cliente — o vínculo por oid (imutável) segue valendo
        var second = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(directoryId, objectId, "outro-email@secco.test")));

        second.IsSuccess.Should().BeTrue("após o vínculo, o casamento é por oid, não por e-mail (ADR-0026)");
        second.Value.Email.Should().Be(email);
    }

    [Fact]
    public async Task ProcessAsync_WithoutFederation_RejectsWithGenericError()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        var email = UniqueEmail();
        await CreateUserAsync(admin, tenantId, email);

        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(Guid.NewGuid(), Guid.NewGuid(), email)));

        result.IsFailure.Should().BeTrue("tenant sem federação não autentica por AD — fail-closed");
        result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
    }

    [Fact]
    public async Task ProcessAsync_WithDisabledFederation_RejectsWithGenericError()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        var directoryId = Guid.NewGuid();
        var email = UniqueEmail();
        await CreateUserAsync(admin, tenantId, email);
        await UpsertFederationAsync(admin, tenantId, directoryId, enabled: false);

        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(directoryId, Guid.NewGuid(), email)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
    }

    [Fact]
    public async Task ProcessAsync_WithMismatchedDirectory_RejectsWithGenericError()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        var email = UniqueEmail();
        await CreateUserAsync(admin, tenantId, email);
        await UpsertFederationAsync(admin, tenantId, Guid.NewGuid());

        // tid de OUTRO diretório: e-mail igual não basta — o pin de diretório barra (ADR-0026)
        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(Guid.NewGuid(), Guid.NewGuid(), email)));

        result.IsFailure.Should().BeTrue("um diretório qualquer não autentica usuário de tenant que não o registrou");
        result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
    }

    [Fact]
    public async Task ProcessAsync_WithUnprovisionedEmail_RejectsWithGenericError()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        await UpsertFederationAsync(admin, tenantId, Guid.NewGuid());

        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(Guid.NewGuid(), Guid.NewGuid(), UniqueEmail())));

        result.IsFailure.Should().BeTrue("o AD nunca decide quem tem acesso — usuário precisa estar provisionado");
        result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
    }

    [Fact]
    public async Task ProcessAsync_WithInactiveTenant_RejectsWithGenericError()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        var directoryId = Guid.NewGuid();
        var email = UniqueEmail();
        await CreateUserAsync(admin, tenantId, email);
        await UpsertFederationAsync(admin, tenantId, directoryId);
        (await admin.PostAsync($"/api/v1/tenants/{tenantId}/deactivate", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(directoryId, Guid.NewGuid(), email)));

        result.IsFailure.Should().BeTrue("tenant desativado não autentica (ADR-0005/0026)");
        result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
    }

    [Fact]
    public async Task ProcessAsync_WithMissingClaims_RejectsWithGenericError()
    {
        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(tid: null, oid: null, email: UniqueEmail())));

        result.IsFailure.Should().BeTrue("token sem tid/oid não identifica diretório nem conta");
        result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
    }

    [Fact]
    public async Task ProcessAsync_WithoutEmailClaimOnFirstLogin_RejectsWithGenericError()
    {
        var admin = CreateAdminClient();
        var tenantId = await CreateTenantAsync(admin);
        var directoryId = Guid.NewGuid();
        await CreateUserAsync(admin, tenantId, UniqueEmail());
        await UpsertFederationAsync(admin, tenantId, directoryId);

        var result = await WithScopeAsync(services =>
            services.GetRequiredService<EntraSignInProcessor>()
                .ProcessAsync(ExternalPrincipal(directoryId, Guid.NewGuid(), email: null)));

        result.IsFailure.Should().BeTrue("sem claim email não há como casar o primeiro login");
        result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
    }
}
