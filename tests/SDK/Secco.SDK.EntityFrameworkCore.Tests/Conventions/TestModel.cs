using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore;
using Secco.SharedKernel.Entities;

namespace Secco.SDK.EntityFrameworkCore.Tests.Conventions;

public enum Severity
{
    Info = 0,
    Error = 1,
}

public sealed class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public Severity Level { get; set; }

    public decimal Balance { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<LogEntry> Entries { get; set; } = [];
}

public sealed class LogEntry : BaseEntity
{
    public string Message { get; set; } = string.Empty;

    public double DurationMs { get; set; }

    /// <summary>Decimal que é quantidade: override explícito previsto na ADR-0017.</summary>
    [Column("qt_items")]
    public decimal Items { get; set; }

    public DateTime OccurredAt { get; set; }

    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

public sealed class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
}

public sealed class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Associativa: PK composta cujos membros são FKs → id_pfk_ (ADR-0017).</summary>
public sealed class UserRole
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public User User { get; set; } = null!;

    public Role Role { get; set; } = null!;
}

/// <summary>Entidade com ToTable explícito — a convention não deve sobrescrever.</summary>
public sealed class Legacy
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : SeccoDbContext(options)
{
    public DbSet<Tenant> Tenants { get; set; }

    public DbSet<LogEntry> LogEntries { get; set; }

    public DbSet<User> Users { get; set; }

    public DbSet<Role> Roles { get; set; }

    public DbSet<UserRole> UserRoles { get; set; }

    public DbSet<Legacy> Legacies { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasIndex(t => t.Slug).IsUnique();

        modelBuilder.Entity<LogEntry>().HasIndex(e => e.OccurredAt);

        modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });

        modelBuilder.Entity<Legacy>().ToTable("legacy_custom");
    }
}
