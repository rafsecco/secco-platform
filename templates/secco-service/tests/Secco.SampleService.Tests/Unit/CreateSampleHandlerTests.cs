using FluentAssertions;
using Secco.SampleService.Application;
using Secco.SampleService.Application.Samples;
using Secco.SampleService.Domain.Samples;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SampleService.Tests.Unit;

/// <summary>Exemplo de teste unitário de handler (ADR-0012): sem infraestrutura, fake da porta.</summary>
public class CreateSampleHandlerTests
{
    private sealed class FakeRepository : ISampleRepository
    {
        public List<Sample> Added { get; } = [];

        public Task AddAsync(Sample sample, CancellationToken cancellationToken = default)
        {
            Added.Add(sample);
            return Task.CompletedTask;
        }

        public Task<Sample?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.FirstOrDefault(sample => sample.Id == id));

        public Task<PagedResult<Sample>> SearchAsync(SampleSearchCriteria criteria, CancellationToken cancellationToken = default) =>
            Task.FromResult(PagedResult.Create(Added, criteria.EffectivePage, Added.Count));
    }

    private static readonly SampleServiceOptions Options = new();

    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsDto()
    {
        var repository = new FakeRepository();
        var handler = new CreateSampleHandler(repository, Options);

        var result = await handler.HandleAsync(new CreateSampleCommand("meu sample"));

        result.IsSuccess.Should().BeTrue();
        repository.Added.Should().ContainSingle().Which.Id.Should().Be(result.Value.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithoutName_ReturnsValidationFailure(string? name)
    {
        var handler = new CreateSampleHandler(new FakeRepository(), Options);

        var result = await handler.HandleAsync(new CreateSampleCommand(name));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SampleServiceErrors.Samples.NameRequired);
    }

    [Fact]
    public async Task Handle_WithNameAboveLimit_ReturnsValidationFailure()
    {
        var handler = new CreateSampleHandler(new FakeRepository(), Options);

        var result = await handler.HandleAsync(
            new CreateSampleCommand(new string('x', Options.MaxNameLength + 1)));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}
