using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.LogStream.Infrastructure.Contexts;

/// <summary>
/// Contexto de dados do LogStream sobre o banco do tenant atual (ADR-0005) —
/// a connection string vem do <c>ITenantConnectionFactory</c> a cada requisição.
/// Herda de <see cref="SeccoDbContext"/>: nomenclatura da ADR-0017 aplicada por convention.
/// DbSets chegam com as fases 4.3+ (Log, LogProcess, ApiCallLog).
/// </summary>
public sealed class LogStreamDbContext(DbContextOptions<LogStreamDbContext> options)
    : SeccoDbContext(options);
