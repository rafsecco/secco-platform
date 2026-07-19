using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore.Conventions;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Cryptography;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SecureGate.Infrastructure.OpenIddict;

namespace Secco.SecureGate.Infrastructure.Contexts;

/// <summary>
/// Contexto do banco de PLATAFORMA do SecureGate (<c>secco_securegate</c>, ADR-0022) —
/// identidade não é dado de tenant: usuários, roles/permissões por tenant, clients OIDC
/// e o catálogo de tenants vivem aqui, e nenhum outro produto acessa este banco.
/// Herda de <c>IdentityDbContext</c> (exigência do ASP.NET Identity); a
/// <see cref="SeccoNamingConvention"/> é registrada pelo caminho público do SDK —
/// exatamente o cenário para o qual ela foi mantida pública.
/// </summary>
/// <remarks>
/// O <paramref name="connectionStringCipher"/> é injetado pelo container (ADR-0025): no
/// caminho de aplicação (<c>AddDbContext</c>) ele cifra/decifra a connection string do
/// catálogo. Os caminhos <b>sem</b> DI (migrations e design-time, que só materializam o
/// schema — o value converter não altera o snapshot) constroem o contexto sem cipher.
/// </remarks>
public sealed class SecureGateDbContext(
    DbContextOptions<SecureGateDbContext> options,
    IConnectionStringCipher? connectionStringCipher = null)
    : IdentityDbContext<User, Role, Guid, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>(options)
{
    /// <summary>
    /// Teto da COLUNA <c>ds_connection_string</c> (ADR-0025): o texto cifrado é maior que o
    /// plaintext (nonce + tag + base64). O teto de 2000 do plaintext segue no domínio.
    /// </summary>
    private const int ConnectionStringColumnMaxLength = 4000;

    private readonly IConnectionStringCipher? _connectionStringCipher = connectionStringCipher;

    /// <summary>Catálogo de tenants da plataforma (tabela <c>tb_tenants</c>).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Bancos por (tenant, produto) do catálogo (tabela <c>tb_tenant_databases</c>).</summary>
    public DbSet<TenantDatabase> TenantDatabases => Set<TenantDatabase>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // ADR-0017 nas colunas: automática (Identity/OpenIddict não fixam nomes de coluna)
        configurationBuilder.Conventions.Add(_ => new SeccoNamingConvention());
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ADR-0017 nas TABELAS: Identity/OpenIddict as nomeiam explicitamente (AspNetUsers,
        // OpenIddictApplications...) e configuração explícita vence a convention — re-nomeamos.
        builder.Entity<User>(user =>
        {
            user.ToTable("tb_users");
            user.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("uk_users_ds_normalized_user_name");
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("idx_users_ds_normalized_email");
            user.HasIndex(u => u.TenantId).HasDatabaseName("idx_users_id_fk_tenant");
            user.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Role>(role =>
        {
            role.ToTable("tb_roles");

            // O Identity cria RoleNameIndex ÚNICO GLOBAL em NormalizedName — errado no modelo
            // multi-tenant: o MESMO nome de role existe em vários tenants (ADR-0021). A
            // unicidade real é o índice por (TenantId, NormalizedName) abaixo.
            if (role.Metadata.FindProperty(nameof(Role.NormalizedName)) is { } normalizedName
                && role.Metadata.FindIndex([normalizedName]) is { } globalNameIndex)
            {
                role.Metadata.RemoveIndex(globalNameIndex);
            }

            // Nome de role é único POR TENANT (ADR-0021), não globalmente
            role.HasIndex(r => new { r.TenantId, r.NormalizedName }).IsUnique()
                .HasDatabaseName("uk_roles_id_fk_tenant_ds_normalized_name");
            role.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserClaim>().ToTable("tb_user_claims");
        builder.Entity<UserRole>().ToTable("tb_user_roles");
        builder.Entity<UserLogin>().ToTable("tb_user_logins");
        builder.Entity<RoleClaim>().ToTable("tb_role_claims");
        builder.Entity<UserToken>().ToTable("tb_user_tokens");

        builder.Entity<OidcApplication>(application =>
        {
            application.ToTable("tb_oidc_applications");
            application.Property(a => a.Roles).HasMaxLength(OidcApplication.RolesMaxLength);
        });
        builder.Entity<OidcAuthorization>().ToTable("tb_oidc_authorizations");
        builder.Entity<OidcScope>().ToTable("tb_oidc_scopes");
        builder.Entity<OidcToken>().ToTable("tb_oidc_tokens");

        builder.Entity<Tenant>(tenant =>
            tenant.HasIndex(t => t.Slug).IsUnique());

        builder.Entity<TenantDatabase>(database =>
        {
            database.Property(d => d.Product).HasMaxLength(TenantDatabase.ProductMaxLength);

            var connectionString = database.Property(d => d.ConnectionString)
                .HasMaxLength(ConnectionStringColumnMaxLength);

            // ADR-0025: cifra no write, decifra no read — nenhum caminho do contexto persiste
            // plaintext. Aplicado só quando há cipher (DI); migrations/design-time só definem o
            // schema, e o value converter não entra no snapshot (não altera o tipo da coluna).
            if (_connectionStringCipher is { } cipher)
            {
                connectionString.HasConversion(
                    plaintext => cipher.Encrypt(plaintext),
                    stored => cipher.Decrypt(stored));
            }

            // Um banco por (tenant, produto) — o par é a identidade natural do catálogo
            database.HasIndex(d => new { d.TenantId, d.Product }).IsUnique();

            // Cascade: o banco é dado intrínseco do tenant (diferente de users/roles, Restrict)
            database.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
