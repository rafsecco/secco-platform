using FluentAssertions;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.SharedKernel.Tests.Pagination;

public class PageRequestTests
{
    [Fact]
    public void Constructor_WithoutArguments_UsesFirstPageAndDefaultSize()
    {
        var request = new PageRequest();

        request.Page.Should().Be(PageRequest.FirstPage);
        request.Size.Should().Be(PageRequest.DefaultSize);
    }

    [Fact]
    public void Constructor_WithValidValues_KeepsThem()
    {
        var request = new PageRequest(page: 3, size: 50);

        request.Page.Should().Be(3);
        request.Size.Should().Be(50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_WhenPageBelowFirst_NormalizesToFirstPage(int page)
    {
        var request = new PageRequest(page);

        request.Page.Should().Be(PageRequest.FirstPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Constructor_WhenSizeNotPositive_NormalizesToDefaultSize(int size)
    {
        var request = new PageRequest(size: size);

        request.Size.Should().Be(PageRequest.DefaultSize);
    }

    [Fact]
    public void Constructor_WhenSizeAboveMax_ClampsToMaxSize()
    {
        var request = new PageRequest(size: PageRequest.MaxSize + 1);

        request.Size.Should().Be(PageRequest.MaxSize);
    }

    [Fact]
    public void Skip_OnFirstPage_ReturnsZero()
    {
        var request = new PageRequest(page: 1, size: 20);

        request.Skip.Should().Be(0);
    }

    [Fact]
    public void Skip_OnLaterPage_ReturnsOffsetOfPreviousPages()
    {
        var request = new PageRequest(page: 4, size: 25);

        request.Skip.Should().Be(75);
    }

    [Fact]
    public void Default_Always_EqualsParameterlessRequest()
    {
        PageRequest.Default.Should().Be(new PageRequest());
    }

    [Fact]
    public void Equals_WhenSamePageAndSize_ReturnsTrue()
    {
        new PageRequest(2, 30).Should().Be(new PageRequest(2, 30));
    }
}
