using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.LogStream.Domain.ApiCalls;

namespace Secco.LogStream.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="ApiCallLog"/>. Nomes vêm da convention (ADR-0017);
/// aqui só índices de consulta (período, sucesso e correlação são os acessos dominantes).
/// </summary>
internal sealed class ApiCallLogConfiguration : IEntityTypeConfiguration<ApiCallLog>
{
    public void Configure(EntityTypeBuilder<ApiCallLog> builder)
    {
        builder.HasIndex(call => call.CreatedAt);
        builder.HasIndex(call => new { call.CreatedAt, call.IsSuccess });
        builder.HasIndex(call => call.CorrelationId);
    }
}
