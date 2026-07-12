using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Domain.ApiCalls;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.LogStream.Infrastructure;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.LogStream.Infrastructure.Retention;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

public class LogRetentionTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private LogStreamDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LogStreamDbContext>()
            .UseSqlServer(factory.GetTenantConnectionString("secco_logstream_alfa"))
            .Options);

    private static void Backdate(LogStreamDbContext context, object entity, DateTimeOffset createdAt) =>
        context.Entry(entity).Property("CreatedAt").CurrentValue = createdAt;

    [Fact]
    public async Task PurgeTenant_Always_DeletesOnlyBeyondWindowIncludingDetailsByCascade()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-40);
        Guid oldEntryId, recentEntryId, oldProcessId, oldDetailId, oldApiCallId;

        await using (var seed = CreateContext())
        {
            var oldEntry = new LogEntry(LogEntryLevel.Information, "antigo");
            var recentEntry = new LogEntry(LogEntryLevel.Information, "recente");
            var oldProcess = new LogProcess("ProcessoAntigo");
            var oldDetail = new LogProcessDetail(oldProcess.Id, LogEntryLevel.Error, "passo antigo");
            var oldApiCall = new ApiCallLog("https://api.exemplo.com/antiga", "GET", true);

            seed.AddRange(oldEntry, recentEntry, oldProcess, oldDetail, oldApiCall);
            Backdate(seed, oldEntry, old);
            Backdate(seed, oldProcess, old);
            Backdate(seed, oldDetail, old);
            Backdate(seed, oldApiCall, old);
            await seed.SaveChangesAsync();

            (oldEntryId, recentEntryId, oldProcessId, oldDetailId, oldApiCallId) =
                (oldEntry.Id, recentEntry.Id, oldProcess.Id, oldDetail.Id, oldApiCall.Id);
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var (entries, processes, apiCalls) = await LogRetentionWorker.PurgeTenantAsync(
            LogStreamDatabaseProvider.SqlServer,
            factory.GetTenantConnectionString("secco_logstream_alfa"), cutoff, CancellationToken.None);

        entries.Should().BeGreaterThanOrEqualTo(1);
        processes.Should().BeGreaterThanOrEqualTo(1);
        apiCalls.Should().BeGreaterThanOrEqualTo(1);

        await using var verify = CreateContext();

        (await verify.LogEntries.AnyAsync(e => e.Id == oldEntryId)).Should().BeFalse("além da janela");
        (await verify.LogEntries.AnyAsync(e => e.Id == recentEntryId)).Should().BeTrue("dentro da janela");
        (await verify.LogProcesses.AnyAsync(p => p.Id == oldProcessId)).Should().BeFalse();
        (await verify.LogProcessDetails.AnyAsync(d => d.Id == oldDetailId))
            .Should().BeFalse("o cascade da FK apaga os details junto do processo");
        (await verify.ApiCallLogs.AnyAsync(c => c.Id == oldApiCallId)).Should().BeFalse();
    }
}
