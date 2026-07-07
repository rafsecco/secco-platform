using FluentAssertions;
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.SharedKernel.Tests.Entities;

public class BaseEntityTests
{
    private sealed class Order : BaseEntity
    {
        public Order()
        {
        }

        public Order(Guid id)
            : base(id)
        {
        }

        public void Ship() => Raise(new OrderShipped());
    }

    private sealed class Customer : BaseEntity
    {
        public Customer(Guid id)
            : base(id)
        {
        }
    }

    private sealed record OrderShipped : IDomainEvent;

    [Fact]
    public void Constructor_WithoutId_GeneratesGuidVersion7()
    {
        var order = new Order();

        order.Id.Should().NotBe(Guid.Empty);
        order.Id.Version.Should().Be(7);
    }

    [Fact]
    public void Constructor_WithId_KeepsGivenId()
    {
        var id = Guid.NewGuid();

        var order = new Order(id);

        order.Id.Should().Be(id);
    }

    [Fact]
    public void Constructor_WithEmptyId_ThrowsDomainInvariantException()
    {
        var act = () => new Order(Guid.Empty);

        act.Should().Throw<DomainInvariantException>();
    }

    [Fact]
    public void Equals_WhenSameTypeAndSameId_ReturnsTrue()
    {
        var id = Guid.NewGuid();

        var left = new Order(id);
        var right = new Order(id);

        left.Equals(right).Should().BeTrue();
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenSameIdButDifferentTypes_ReturnsFalse()
    {
        var id = Guid.NewGuid();

        var order = new Order(id);
        var customer = new Customer(id);

        order.Equals(customer).Should().BeFalse();
        (order == customer).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenDifferentIds_ReturnsFalse()
    {
        var left = new Order();
        var right = new Order();

        left.Equals(right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void Equals_WhenComparedToNull_ReturnsFalse()
    {
        var order = new Order();

        order.Equals(null).Should().BeFalse();
        (order == null).Should().BeFalse();
        (null == order).Should().BeFalse();
    }

    [Fact]
    public void Raise_Always_AccumulatesDomainEvents()
    {
        var order = new Order();

        order.Ship();
        order.Ship();

        order.DomainEvents.Should().HaveCount(2);
        order.DomainEvents.Should().AllBeOfType<OrderShipped>();
    }

    [Fact]
    public void ClearDomainEvents_Always_RemovesAllAccumulatedEvents()
    {
        var order = new Order();
        order.Ship();

        order.ClearDomainEvents();

        order.DomainEvents.Should().BeEmpty();
    }
}
