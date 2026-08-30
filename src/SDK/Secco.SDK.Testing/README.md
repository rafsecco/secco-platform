# Secco.SDK.Testing

Base das factories de teste de integração da Secco Platform (ADR-0012, ADR-0027). Sobe a API real no ambiente `Testing` contra um SQL Server real, emite tokens compatíveis com a configuração de autenticação da plataforma e monta as chaves de tenancy e de permissões.

Produto novo **herda** desta base e não escreve a própria `ApiFactory` — foi a duplicação entre cinco cópias divergentes que motivou o pacote.

## Uso

```csharp
public sealed class MeuProdutoApiFactory : SeccoApiFactory<Program>
{
    protected override string Audience => "secco-meuproduto";

    public Guid TenantAlfa { get; } = Guid.NewGuid();

    protected override Task MigrateAsync(IServiceProvider services) =>
        services.MigrateMeuProdutoTenantDatabasesAsync();

    protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
    {
        AddTenant(settings, TenantAlfa, GetConnectionStringFor("secco_meuproduto_alfa"));
        AddRolePermissions(settings, DefaultTestRole, "recursos:read", "recursos:write");
    }
}
```

Nos testes: `factory.CreateToken(factory.TenantAlfa)` para token com tenant e role, `factory.CreateTokenWithScopes("catalog:meuproduto")` para o formato de client credentials, e `factory.EnsureDatabaseMigratedAsync()` antes do primeiro acesso a dados.

## O que a base fixa

- **`ConfigureWebHost` é selado.** A extensão acontece por `ConfigureTestConfiguration`, `ConfigureTestServices` e `OnInitializedAsync`. Selar é o que impede uma cópia divergente de reaparecer por herança.
- **`Audience` e `MigrateAsync` são abstratos** — esquecer de definí-los é erro de compilação, não teste vermelho no CI.
- **Chave de assinatura aleatória por instância** (32 bytes), nunca uma constante. Uma chave embutida num pacote publicado seria de conhecimento público (ADR-0020).
- **Nome de database validado por allowlist** antes de entrar em `CREATE`/`DROP DATABASE`, com sufixo de execução por instância — duas suítes concorrentes não colidem.

## Instância de SQL Server

Por padrão cada suíte sobe o **próprio container** (o CI segue hermético). Definindo `SECCO_TEST_SQLSERVER` com a connection string de um servidor existente, a base usa aquela instância e não sobe container — o caminho para máquinas onde N containers saturam o Docker. Nesse modo, os databases criados são derrubados no encerramento, best-effort.

O `docker compose up -d` da raiz do monorepo serve como essa instância.
