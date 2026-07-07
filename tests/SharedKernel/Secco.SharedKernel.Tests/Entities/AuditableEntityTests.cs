using FluentAssertions;
using Secco.SharedKernel.Entities;
using Xunit;

namespace Secco.SharedKernel.Tests.Entities;

public class AuditableEntityTests
{
    private sealed class Invoice : AuditableEntity, ISoftDeletable
    {
        public bool IsDeleted { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }
    }

    [Fact]
    public void Constructor_Always_InheritsBaseEntityIdGeneration()
    {
        var invoice = new Invoice();

        invoice.Id.Should().NotBe(Guid.Empty);
        invoice.Should().BeAssignableTo<BaseEntity>();
    }

    [Fact]
    public void AuditFields_BeforeInterceptorRuns_AreEmpty()
    {
        var invoice = new Invoice();

        invoice.CreatedAt.Should().Be(default);
        invoice.CreatedBy.Should().BeNull();
        invoice.UpdatedAt.Should().BeNull();
        invoice.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public void AuditFields_WhenSetByInterceptor_KeepValues()
    {
        var now = DateTimeOffset.UtcNow;
        var invoice = new Invoice
        {
            CreatedAt = now,
            CreatedBy = "user-1",
            UpdatedAt = now,
            UpdatedBy = "user-2",
        };

        invoice.CreatedAt.Should().Be(now);
        invoice.CreatedBy.Should().Be("user-1");
        invoice.UpdatedAt.Should().Be(now);
        invoice.UpdatedBy.Should().Be("user-2");
    }

    [Fact]
    public void SoftDeletable_WhenMarkedDeleted_KeepsDeletionMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var invoice = new Invoice { IsDeleted = true, DeletedAt = now };

        invoice.IsDeleted.Should().BeTrue();
        invoice.DeletedAt.Should().Be(now);
    }
}
