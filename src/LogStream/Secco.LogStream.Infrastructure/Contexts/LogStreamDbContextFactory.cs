using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Secco.LogStream.Infrastructure.Contexts;

/// <summary>
/// Fábrica de design-time para o <c>dotnet ef</c> (gerar migrations sem subir a API).
/// A connection string é fictícia: geração de migration não conecta ao banco.
/// </summary>
public sealed class LogStreamDbContextFactory : IDesignTimeDbContextFactory<LogStreamDbContext>
{
    public LogStreamDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LogStreamDbContext>()
            .UseSqlServer("Server=design-time;Database=design-time;Encrypt=false")
            .Options);
}
