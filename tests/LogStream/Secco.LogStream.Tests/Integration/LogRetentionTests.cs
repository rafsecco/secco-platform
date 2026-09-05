using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Domain.ApiCalls;
using Secco.LogStream.Domain.Audit;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.LogStream.Infrastructure;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.LogStream.Infrastructure.Retention;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

public class LogRetentionTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private LogStreamDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<LogStreamDbContext>()
			.UseSqlServer(factory.GetConnectionStringFor("secco_logstream_alfa"))
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
		var (entries, processes, apiCalls, _) = await LogRetentionWorker.PurgeTenantAsync(
			LogStreamDatabaseProvider.SqlServer,
			factory.GetConnectionStringFor("secco_logstream_alfa"), cutoff,
			cancellationToken: CancellationToken.None);

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

	[Fact]
	public async Task Purge_WhenOnlyDiagnosticWindowConfigured_KeepsAuditEntries()
	{
		var old = DateTimeOffset.UtcNow.AddDays(-40);
		Guid oldEntryId, oldAuditEntryId;

		await using (var seed = CreateContext())
		{
			var oldEntry = new LogEntry(LogEntryLevel.Information, "log antigo de diagnóstico");
			var oldAuditEntry = new AuditEntry("user-antigo", ActorType.User, "documento.download", occurredAt: old);

			seed.AddRange(oldEntry, oldAuditEntry);
			Backdate(seed, oldEntry, old);
			await seed.SaveChangesAsync();

			(oldEntryId, oldAuditEntryId) = (oldEntry.Id, oldAuditEntry.Id);
		}

		var diagnosticCutoff = DateTimeOffset.UtcNow.AddDays(-30);

		// auditCutoff explicitamente omitido (null): só a janela de diagnóstico está configurada
		var (entries, _, _, auditEntries) = await LogRetentionWorker.PurgeTenantAsync(
			LogStreamDatabaseProvider.SqlServer,
			factory.GetConnectionStringFor("secco_logstream_alfa"), diagnosticCutoff);

		entries.Should().BeGreaterThanOrEqualTo(1);
		auditEntries.Should().Be(0, "a janela de diagnóstico NUNCA leva a trilha de auditoria junto");

		await using var verify = CreateContext();

		(await verify.LogEntries.AnyAsync(e => e.Id == oldEntryId)).Should().BeFalse("além da janela de diagnóstico");
		(await verify.AuditEntries.AnyAsync(a => a.Id == oldAuditEntryId)).Should().BeTrue(
			"sem janela de auditoria configurada, a trilha nunca expira — mesmo com o registro além do que seria a janela de diagnóstico");
	}

	[Fact]
	public async Task Purge_WhenAuditWindowConfigured_ExpungesAuditEntriesBeyondWindowOnly()
	{
		var old = DateTimeOffset.UtcNow.AddDays(-400);
		var recent = DateTimeOffset.UtcNow.AddDays(-1);
		Guid oldAuditEntryId, recentAuditEntryId;

		await using (var seed = CreateContext())
		{
			var oldAuditEntry = new AuditEntry("user-antigo", ActorType.User, "documento.download", occurredAt: old);
			var recentAuditEntry = new AuditEntry("user-recente", ActorType.User, "documento.download", occurredAt: recent);

			seed.AddRange(oldAuditEntry, recentAuditEntry);

			// O expurgo corta pelo CreatedAt do servidor, não pelo OccurredAt declarado — por
			// isso o seed precisa envelhecer o carimbo do servidor explicitamente.
			seed.Entry(oldAuditEntry).Property(nameof(AuditEntry.CreatedAt)).CurrentValue = old;

			await seed.SaveChangesAsync();

			(oldAuditEntryId, recentAuditEntryId) = (oldAuditEntry.Id, recentAuditEntry.Id);
		}

		var auditCutoff = DateTimeOffset.UtcNow.AddDays(-365);

		var (_, _, _, auditEntries) = await LogRetentionWorker.PurgeTenantAsync(
			LogStreamDatabaseProvider.SqlServer,
			factory.GetConnectionStringFor("secco_logstream_alfa"), diagnosticCutoff: null, auditCutoff: auditCutoff);

		auditEntries.Should().BeGreaterThanOrEqualTo(1);

		await using var verify = CreateContext();

		(await verify.AuditEntries.AnyAsync(a => a.Id == oldAuditEntryId)).Should().BeFalse("além da janela de auditoria");
		(await verify.AuditEntries.AnyAsync(a => a.Id == recentAuditEntryId)).Should().BeTrue("dentro da janela de auditoria");
	}

	[Fact]
	public async Task Purge_WhenOccurredAtIsBackdated_KeepsTheEntry()
	{
		// ADR-0020: o OccurredAt é declarado pelo chamador. Se ele governasse a retenção, um
		// valor forjado no passado apagaria a própria trilha antes da hora — input externo
		// comandando operação destrutiva. Quem corta é o CreatedAt, carimbado pelo servidor.
		Guid backdatedId;

		await using (var seed = CreateContext())
		{
			var backdated = new AuditEntry(
				"ator-mal-intencionado",
				ActorType.User,
				"documento.download",
				occurredAt: DateTimeOffset.UtcNow.AddYears(-10));

			seed.Add(backdated);
			await seed.SaveChangesAsync();

			backdatedId = backdated.Id;
		}

		await LogRetentionWorker.PurgeTenantAsync(
			LogStreamDatabaseProvider.SqlServer,
			factory.GetConnectionStringFor("secco_logstream_alfa"),
			diagnosticCutoff: null,
			auditCutoff: DateTimeOffset.UtcNow.AddDays(-1));

		await using var verify = CreateContext();

		(await verify.AuditEntries.AnyAsync(a => a.Id == backdatedId))
			.Should().BeTrue("o OccurredAt declarado não pode antecipar o expurgo");
	}
}
