using FluentAssertions;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.SharedKernel.Tests.Pagination;

public class PagedResultTests
{
    [Fact]
    public void Create_WithRequest_EchoesPageAndSize()
    {
        var request = new PageRequest(page: 2, size: 10);

        var result = PagedResult.Create(["a", "b"], request, totalCount: 25);

        result.Items.Should().ContainInOrder("a", "b");
        result.Page.Should().Be(2);
        result.Size.Should().Be(10);
        result.TotalCount.Should().Be(25);
    }

    [Fact]
    public void Empty_Always_HasNoItemsAndZeroTotal()
    {
        var result = PagedResult.Empty<string>(PageRequest.Default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Theory]
    [InlineData(25, 10, 3)]
    [InlineData(30, 10, 3)]
    [InlineData(1, 10, 1)]
    [InlineData(0, 10, 0)]
    public void TotalPages_Always_RoundsUp(long totalCount, int size, int expectedTotalPages)
    {
        var result = new PagedResult<int>([], page: 1, size: size, totalCount: totalCount);

        result.TotalPages.Should().Be(expectedTotalPages);
    }

    [Fact]
    public void HasNextPage_WhenNotOnLastPage_ReturnsTrue()
    {
        var result = new PagedResult<int>([1], page: 1, size: 10, totalCount: 25);

        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void HasNextPage_WhenOnLastPage_ReturnsFalse()
    {
        var result = new PagedResult<int>([1], page: 3, size: 10, totalCount: 25);

        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WhenPageBelowFirst_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new PagedResult<int>([], page: 0, size: 10, totalCount: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WhenSizeNotPositive_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new PagedResult<int>([], page: 1, size: 0, totalCount: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WhenTotalCountNegative_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new PagedResult<int>([], page: 1, size: 10, totalCount: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
