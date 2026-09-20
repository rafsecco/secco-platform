# Credenciais por e-mail (entrega B) — plano de implementação

> **Para quem executa:** SUB-SKILL OBRIGATÓRIA: use `superpowers:subagent-driven-development` (recomendado) ou `superpowers:executing-plans` para implementar tarefa a tarefa. Os passos usam caixas (`- [ ]`) para acompanhamento.

**Objetivo:** tirar a senha das mãos do admin — a conta nasce sem senha e a pessoa a define por um link de convite; qualquer pessoa pede "esqueci minha senha"; o usuário troca a própria senha; e todo evento de senha encerra as sessões abertas.

**Arquitetura:** nasce o pacote fino `Secco.SDK.Email` (porta + adaptadores SMTP/SendGrid promovidos do NotificationHub), o SecureGate passa a enviar e-mail e a guardar as chaves de Data Protection no próprio banco, os links são tokens do ASP.NET Identity (o `SecurityStamp` embutido dá uso único de graça), e as telas vivem no SecureGate como Razor Pages com antiforgery. Nenhum endpoint público aceita token + senha.

**Stack:** .NET 10, ASP.NET Identity, OpenIddict 7.5, EF Core 10 (SQL Server + PostgreSQL), MailKit/SendGrid, Razor Pages, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** [`docs/superpowers/specs/2026-09-17-credenciais-por-email-design.md`](../specs/2026-09-17-credenciais-por-email-design.md)
**ADRs:** [ADR-0033](../../adr/secco-platform-adrs.md) (capacidade de e-mail e ciclo de credencial), [ADR-0032](../../adr/secco-platform-adrs.md) (revogação de sessão), [ADR-0034](../../adr/secco-platform-adrs.md) (idempotência), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança).

## Restrições globais

- **Estilo:** CRLF e **tabs** em todo arquivo do repositório; XML docs em português em tudo que é público; `warnings = erros` no build Release.
- **Camadas (ADR-0002):** `Application` não conhece `Microsoft.AspNetCore.*`, `Microsoft.Extensions.Options` nem EF Core — só abstrações de DI. Toda porta nova nasce em `Application`, o adaptador em `Infrastructure`, a composição na `Api`.
- **Erros (ADR-0004):** negócio via `Result<T>`/`Error` de `Secco.SharedKernel`; nunca exceção para fluxo.
- **Options:** `services.AddOptions<T>().BindConfiguration("Chave")`; a Application recebe o POCO por adaptador na composição (nunca `IOptions<T>`).
- **Idempotência (ADR-0034):** `PUT`/`DELETE` idempotentes de fato; efeito colateral só na transição real; endpoint anônimo com efeito externo tem limite de taxa e teste de repetição.
- **Segredos:** senha, token de link e chave de API nunca entram em log, mensagem de erro, resposta HTTP ou nome de teste.
- **Banco (ADR-0017):** nomes vêm da `SeccoNamingConvention`; tabela nova recebe `ToTable("tb_...")` explícito e migrations **nos dois engines** (`Secco.SecureGate.Migrations.SqlServer` e `.Postgres`).
- **Testes (ADR-0012):** toda tarefa entrega teste no mesmo commit. Rodar a suíte do produto: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release`.
- **Commits:** Conventional Commits, um por tarefa, com `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` ao final. Commit direto na `main`, sem branch nem PR. **Nunca `git push`** — a publicação é a Tarefa 12, e só com autorização.
- **Cuidado com CRLF:** nunca usar `sed -i` nos arquivos versionados (achata CRLF → LF). Conferir `git diff --stat` depois de cada edição em massa.

## Estrutura de arquivos

**Pacote novo** `src/SDK/Secco.SDK.Email/` — `ISeccoEmailSender`, `SeccoEmailOptions`, `SeccoEmailProvider`, os dois adaptadores, o validador e `AddSeccoEmail(sectionKey)`. Sem dependência de produto nenhum.

**SecureGate — Application** (`src/SecureGate/Secco.SecureGate.Application/Credentials/`): portas e casos de uso — `ICredentialMailer`, `ICredentialAuditor`, `ICredentialTokens`, `InviteUserHandler`, `RequestPasswordResetHandler`, `ResetPasswordHandler`, `ChangeOwnPasswordHandler`, `SetLocalLoginHandler`, `CredentialErrors` (dentro de `SecureGateErrors.Credentials`).

**SecureGate — Infrastructure** (`.../Credentials/`): `IdentityCredentialTokens` (provedores do Identity), `SeccoEmailCredentialMailer` (usa `ISeccoEmailSender`), `LogStreamCredentialAuditor` (reusa o `HttpClient` da identidade de auditoria), `CredentialOptions`/validador, `DataProtectionKeyContext` no `SecureGateDbContext`.

**SecureGate — Api**: `Pages/Account/{SetPassword,Forgot,ResetPassword,ChangePassword}.cshtml(.cs)` + `Pages/Shared/_AccountLayout.cshtml` (o CSS de hoje sai do `Login.cshtml` e passa a ser compartilhado), `Extensions/SecureGateCredentialsExtensions.cs` (options, token providers, Data Protection, rate limiter), rotas novas em `Endpoints/UserEndpoints.cs`.

**AdminPortal**: `Services/IUserAdminService.cs` e `Components/Pages/{TenantManagement,UserManagement}.razor`.

**Testes**: `tests/SDK/Secco.SDK.Email.Tests/` (novo) e, no SecureGate, `Integration/CredentialFlowTests.cs`, `Integration/PasswordRecoveryTests.cs`, `Integration/LocalLoginToggleTests.cs`, `Integration/FakeEmailSender.cs`, `Unit/CredentialOptionsTests.cs`.

---

### Task 1: `Secco.SDK.Email` — porta e adaptadores promovidos

**Arquivos:**
- Criar: `src/SDK/Secco.SDK.Email/Secco.SDK.Email.csproj`, `README.md`, `ISeccoEmailSender.cs`, `SeccoEmailProvider.cs`, `SeccoEmailOptions.cs`, `SeccoEmailOptionsValidator.cs`, `SeccoSmtpEmailSender.cs`, `SeccoSendGridEmailSender.cs`, `SeccoEmailServiceCollectionExtensions.cs`
- Criar: `tests/SDK/Secco.SDK.Email.Tests/Secco.SDK.Email.Tests.csproj`, `EmailOptionsTests.cs`, `EmailSenderSelectionTests.cs`
- Modificar: `Secco.Platform.slnx`, `Directory.Packages.props` (nada novo — MailKit e SendGrid já existem), `.github/workflows/publish-packages.yml`, `.claude/skills/secco-platform-release/SKILL.md`

**Interfaces:**
- Produz: `Secco.SDK.Email.ISeccoEmailSender.SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)`; `SeccoEmailOptions` (propriedades `Provider`, `ApiKey`, `Host`, `Port`, `UseStartTls`, `Username`, `Password`, `FromAddress`, `FromName`, método `bool TryValidate(string sectionKey, out string? error)`); `SeccoEmailProvider { Smtp = 0, SendGrid = 1 }`; `IServiceCollection AddSeccoEmail(this IServiceCollection services, string sectionKey)`.

- [ ] **Passo 1: criar o projeto e ligá-lo à solution**

```xml
<!-- src/SDK/Secco.SDK.Email/Secco.SDK.Email.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Pacote publicável (ADR-0011): versão via tag git com prefixo sdk-email/v -->
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <MinVerTagPrefix>sdk-email/v</MinVerTagPrefix>
    <Description>Envio de e-mail da Secco Platform (ADR-0033): porta ISeccoEmailSender com adaptadores SMTP (MailKit) e SendGrid, selecionáveis por configuração.</Description>
    <PackageTags>secco;sdk;email;smtp;sendgrid</PackageTags>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MailKit" />
    <PackageReference Include="SendGrid" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
  </ItemGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

</Project>
```

Copiar o `TargetFramework` e demais defaults do `Directory.Build.props` (o projeto não declara nada além do acima). Acrescentar o projeto e o de testes à `Secco.Platform.slnx`, ao lado dos outros do SDK.

- [ ] **Passo 2: escrever os testes que falham**

```csharp
// tests/SDK/Secco.SDK.Email.Tests/EmailOptionsTests.cs
public class EmailOptionsTests
{
	[Fact]
	public void Options_ProviderPadrao_ESmtp() =>
		new SeccoEmailOptions().Provider.Should().Be(SeccoEmailProvider.Smtp);

	[Fact]
	public void Options_SmtpSemHost_Invalida()
	{
		var options = new SeccoEmailOptions { FromAddress = "no-reply@secco.local" };

		options.TryValidate("SecureGate:Email", out var error).Should().BeFalse();
		error.Should().Contain("SecureGate:Email:Host");
	}

	[Fact]
	public void Options_SendGridSemApiKey_Invalida()
	{
		var options = new SeccoEmailOptions
		{
			Provider = SeccoEmailProvider.SendGrid,
			FromAddress = "no-reply@secco.local",
		};

		options.TryValidate("NotificationHub:Email", out var error).Should().BeFalse();
		error.Should().Contain("NotificationHub:Email:ApiKey");
	}

	[Fact]
	public void Options_MensagemDeErro_NuncaCitaSegredo()
	{
		var options = new SeccoEmailOptions { Provider = SeccoEmailProvider.SendGrid, ApiKey = "SG.segredo" };

		options.TryValidate("SecureGate:Email", out var error).Should().BeFalse();
		error.Should().NotContain("SG.segredo");
	}
}
```

```csharp
// tests/SDK/Secco.SDK.Email.Tests/EmailSenderSelectionTests.cs
public class EmailSenderSelectionTests
{
	private static ServiceProvider Build(Dictionary<string, string?> settings)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddSeccoEmail("SecureGate:Email");

		return services.BuildServiceProvider();
	}

	[Fact]
	public void Composicao_ProviderSendGrid_ResolveOAdaptadorDoSendGrid()
	{
		using var provider = Build(new Dictionary<string, string?>
		{
			["SecureGate:Email:Provider"] = "SendGrid",
			["SecureGate:Email:ApiKey"] = "SG.chave-de-teste",
			["SecureGate:Email:FromAddress"] = "no-reply@secco.local",
		});

		provider.GetRequiredService<ISeccoEmailSender>().Should().BeOfType<SeccoSendGridEmailSender>();
	}

	[Fact]
	public void Composicao_ProviderPadrao_ResolveOAdaptadorSmtp()
	{
		using var provider = Build(new Dictionary<string, string?>
		{
			["SecureGate:Email:Host"] = "localhost",
			["SecureGate:Email:FromAddress"] = "no-reply@secco.local",
		});

		provider.GetRequiredService<ISeccoEmailSender>().Should().BeOfType<SeccoSmtpEmailSender>();
	}

	[Fact]
	public async Task SendGrid_QuandoAApiRecusa_Lanca()
	{
		var client = Substitute.For<ISendGridClient>();
		client.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
			.Returns(new Response(HttpStatusCode.BadRequest, new StringContent("payload secreto"), null));

		var sender = new SeccoSendGridEmailSender(client, new SeccoEmailOptions { FromAddress = "no-reply@secco.local" });

		var act = async () => await sender.SendAsync("alguem@secco.local", "Assunto", "Corpo", CancellationToken.None);

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.Which.Message.Should().NotContain("payload secreto");
	}
}
```

- [ ] **Passo 3: rodar e ver falhar**

`dotnet test tests/SDK/Secco.SDK.Email.Tests/Secco.SDK.Email.Tests.csproj -c Release` → falha de compilação (tipos inexistentes).

- [ ] **Passo 4: promover o código do NotificationHub**

Copiar `IEmailSender`, `MailKitEmailSender`, `SendGridEmailSender`, `NotificationHubEmailOptions`, `NotificationHubEmailProvider` e `NotificationHubEmailOptionsValidator` de `src/NotificationHub/Secco.NotificationHub.Infrastructure/Email/` para o pacote, renomeando para `ISeccoEmailSender`, `SeccoSmtpEmailSender`, `SeccoSendGridEmailSender`, `SeccoEmailOptions`, `SeccoEmailProvider`, `SeccoEmailOptionsValidator` e trocando `internal` por `public`. **Comportamento não muda**; a única diferença de forma é a chave da seção virar parâmetro:

```csharp
/// <summary>Valida o conjunto exigido pelo provider selecionado. A mensagem cita NOMES de chave, nunca valores.</summary>
/// <param name="sectionKey">Chave da seção do produto (ex.: <c>SecureGate:Email</c>), só para a mensagem.</param>
/// <param name="error">Descrição do primeiro problema; nula quando válido.</param>
public bool TryValidate(string sectionKey, out string? error)
{
	if (string.IsNullOrWhiteSpace(FromAddress))
	{
		error = $"'{sectionKey}:FromAddress' é obrigatório.";
		return false;
	}

	if (Provider == SeccoEmailProvider.Smtp && string.IsNullOrWhiteSpace(Host))
	{
		error = $"'{sectionKey}:Host' é obrigatório quando o provider é Smtp.";
		return false;
	}

	if (Provider == SeccoEmailProvider.SendGrid && string.IsNullOrWhiteSpace(ApiKey))
	{
		error = $"'{sectionKey}:ApiKey' é obrigatório quando o provider é SendGrid.";
		return false;
	}

	error = null;
	return true;
}
```

A composição repete a forma que o NotificationHub já usa (bind lazy + POCO + client singleton):

```csharp
// src/SDK/Secco.SDK.Email/SeccoEmailServiceCollectionExtensions.cs
/// <summary>Registra a porta de e-mail lendo a seção do produto (ADR-0033: o pacote entrega tipos, não a chave).</summary>
/// <param name="services">Coleção de serviços.</param>
/// <param name="sectionKey">Seção de configuração do produto (ex.: <c>SecureGate:Email</c>).</param>
public static IServiceCollection AddSeccoEmail(this IServiceCollection services, string sectionKey)
{
	ArgumentNullException.ThrowIfNull(services);
	ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

	services.AddOptions<SeccoEmailOptions>().BindConfiguration(sectionKey).ValidateOnStart();
	services.TryAddSingleton<IValidateOptions<SeccoEmailOptions>>(new SeccoEmailOptionsValidator(sectionKey));
	services.TryAddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<SeccoEmailOptions>>().Value);

	// Só é construído quando o provider selecionado é SendGrid — no SMTP não há chave de API.
	services.TryAddSingleton<ISendGridClient>(serviceProvider =>
		new SendGridClient(serviceProvider.GetRequiredService<SeccoEmailOptions>().ApiKey));

	services.TryAddScoped<ISeccoEmailSender>(serviceProvider =>
	{
		var options = serviceProvider.GetRequiredService<SeccoEmailOptions>();

		return options.Provider switch
		{
			SeccoEmailProvider.SendGrid => new SeccoSendGridEmailSender(serviceProvider.GetRequiredService<ISendGridClient>(), options),
			_ => new SeccoSmtpEmailSender(options),
		};
	});

	return services;
}
```

- [ ] **Passo 5: rodar e ver passar**

`dotnet test tests/SDK/Secco.SDK.Email.Tests/Secco.SDK.Email.Tests.csproj -c Release` → verde.

- [ ] **Passo 6: registrar o pacote nos três lugares da skill de release**

1. `.github/workflows/publish-packages.yml`, em `on.push.tags`: `"sdk-email/v*"`.
2. No `case` de mapeamento tag→projeto: `sdk-email/v*) PROJECT="src/SDK/Secco.SDK.Email/Secco.SDK.Email.csproj" ;;` — seguir exatamente a forma das entradas vizinhas.
3. Tabela da seção 4 de `.claude/skills/secco-platform-release/SKILL.md`: linha `| \`sdk-email/v*\` | \`src/SDK/Secco.SDK.Email/Secco.SDK.Email.csproj\` |`.

Escrever o `README.md` do pacote no formato dos outros do SDK (o que é, como registrar, a seção de configuração de exemplo, e o aviso de que a chave da seção é do produto).

- [ ] **Passo 7: commit**

```bash
git add src/SDK/Secco.SDK.Email tests/SDK/Secco.SDK.Email.Tests Secco.Platform.slnx .github/workflows/publish-packages.yml .claude/skills/secco-platform-release/SKILL.md
git commit -m "feat(sdk-email): porta de e-mail e adaptadores SMTP/SendGrid num pacote fino (ADR-0033)"
```

---

### Task 2: NotificationHub adota o pacote e apaga as cópias

**Arquivos:**
- Apagar: `src/NotificationHub/Secco.NotificationHub.Infrastructure/Email/{IEmailSender,MailKitEmailSender,SendGridEmailSender,NotificationHubEmailOptions,NotificationHubEmailProvider,NotificationHubEmailOptionsValidator}.cs`
- Modificar: `src/NotificationHub/Secco.NotificationHub.Infrastructure/NotificationHubInfrastructureExtensions.cs`, `.../Email/SendEmailJob.cs`, `src/NotificationHub/Secco.NotificationHub.Infrastructure/Secco.NotificationHub.Infrastructure.csproj`
- Modificar: `tests/NotificationHub/Secco.NotificationHub.Tests/Integration/FakeEmailSender.cs`
- Apagar: `tests/NotificationHub/Secco.NotificationHub.Tests/Unit/EmailProviderSelectionTests.cs` (o conteúdo virou a Tarefa 1)

**Interfaces:**
- Consome: `ISeccoEmailSender`, `AddSeccoEmail(sectionKey)` da Tarefa 1.
- Produz: nada novo. **A seção `NotificationHub:Email` continua com as mesmas chaves** — adoção sem quebra de configuração.

- [ ] **Passo 1: garantir o teste que prova a não-quebra**

Em `tests/NotificationHub/Secco.NotificationHub.Tests/Integration/NotificationEndpointsTests.cs` já existe cobertura do envio com o `FakeEmailSender`. Antes de mexer, rodar a suíte inteira do produto e anotar o número de testes:

`dotnet test tests/NotificationHub/Secco.NotificationHub.Tests/Secco.NotificationHub.Tests.csproj -c Release`

- [ ] **Passo 2: trocar a porta pelo pacote**

- `ProjectReference` para `..\..\SDK\Secco.SDK.Email\Secco.SDK.Email.csproj` na Infrastructure.
- `SendEmailJob` passa a receber `ISeccoEmailSender` (só o tipo muda; a assinatura de `SendAsync` é idêntica).
- Em `AddNotificationHubInfrastructure`, apagar o bind/validador/POCO/seleção de e-mail e o registro do `ISendGridClient`, deixando **uma linha**: `services.AddSeccoEmail("NotificationHub:Email");`.
- `FakeEmailSender` nos testes passa a implementar `ISeccoEmailSender` e é registrado como antes.
- Um `grep -rn "NotificationHubEmail\|IEmailSender" src tests docs` não pode sobrar com nada.

- [ ] **Passo 3: rodar e comparar**

`dotnet test tests/NotificationHub/Secco.NotificationHub.Tests/Secco.NotificationHub.Tests.csproj -c Release` → mesmo número de testes menos os 5 migrados, todos verdes.

- [ ] **Passo 4: commit**

```bash
git add src/NotificationHub tests/NotificationHub
git commit -m "refactor(notificationhub): adota Secco.SDK.Email e apaga as cópias dos adaptadores"
```

---

### Task 3: chaves de Data Protection no banco do SecureGate

**Arquivos:**
- Modificar: `Directory.Packages.props` (novo `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.9), `src/SecureGate/Secco.SecureGate.Infrastructure/Secco.SecureGate.Infrastructure.csproj`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Contexts/SecureGateDbContext.cs`
- Criar: migration `AddDataProtectionKeys` nos dois assemblies de migrations
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateIdentityExtensions.cs`
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/PlatformSchemaTests.cs`

**Interfaces:**
- Produz: `SecureGateDbContext : IDataProtectionKeyContext` com `DbSet<DataProtectionKey> DataProtectionKeys`; tabela `tb_data_protection_keys`.

- [ ] **Passo 1: escrever o teste que falha**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Integration/PlatformSchemaTests.cs (novo fato)
[Fact]
public async Task DataProtectionKeys_Always_LiveInTheDatabaseWithAdr0017Naming()
{
	await using var context = CreateContext();

	var tables = await context.Database
		.SqlQueryRaw<string>("SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
		.ToListAsync();

	// Sem persistência, as chaves ficam na máquina e todo link de convite/redefinição
	// morre a cada reinício ou instância nova (ADR-0033).
	tables.Should().Contain("tb_data_protection_keys");
}
```

- [ ] **Passo 2: rodar e ver falhar**

`dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter FullyQualifiedName~PlatformSchemaTests` → falha: a tabela não existe.

- [ ] **Passo 3: implementar**

```csharp
// SecureGateDbContext
public sealed class SecureGateDbContext(DbContextOptions<SecureGateDbContext> options)
	: IdentityDbContext<User, Role, Guid, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>(options),
		IDataProtectionKeyContext
{
	/// <summary>
	/// Chaves de Data Protection (ADR-0033). Ficam no banco porque os tokens de convite e de
	/// redefinição — e os cookies de login — são protegidos por elas: no armazenamento padrão
	/// elas vivem na máquina e somem a cada reinício, levando junto todo link pendente.
	/// </summary>
	public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
```

Em `OnModelCreating`, junto das outras tabelas de framework re-nomeadas:

```csharp
builder.Entity<DataProtectionKey>(key =>
{
	key.ToTable("tb_data_protection_keys");
	key.Property(k => k.Id).HasColumnName("id_pk_data_protection_key");
	key.Property(k => k.FriendlyName).HasColumnName("ds_friendly_name");
	key.Property(k => k.Xml).HasColumnName("ds_xml");
});
```

Na composição (`AddSecureGateIdentity`), antes do `AddIdentityCore`:

```csharp
// ADR-0033: chaves compartilhadas por todas as instâncias e sobreviventes a reinício.
// A aplicação (nome) entra no nome do propósito, então não compartilhe com outro produto.
services.AddDataProtection()
	.SetApplicationName("secco-securegate")
	.PersistKeysToDbContext<SecureGateDbContext>();
```

Gerar as migrations nos dois engines (tarefa `ef: migrations add` do VS Code ou):

```bash
dotnet ef migrations add AddDataProtectionKeys --project src/SecureGate/Secco.SecureGate.Migrations.SqlServer --output-dir Migrations
dotnet ef migrations add AddDataProtectionKeys --project src/SecureGate/Secco.SecureGate.Migrations.Postgres --output-dir Migrations
```

- [ ] **Passo 4: rodar e ver passar**

`dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter FullyQualifiedName~PlatformSchemaTests` → verde, inclusive `Migrations_Always_ApplyFromScratch`.

- [ ] **Passo 5: commit**

```bash
git add Directory.Packages.props src/SecureGate tests/SecureGate
git commit -m "feat(securegate): persiste as chaves de Data Protection no banco (ADR-0033)"
```

---

### Task 4: `fl_local_login_enabled` e o que a tela precisa saber

**Arquivos:**
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Identity/IdentityEntities.cs`
- Criar: migration `AddLocalLoginFlag` nos dois assemblies
- Modificar: `src/SecureGate/Secco.SecureGate.Application/Users/{IUserDirectory,UserDetailDto}.cs`, `.../Users/GetUserHandler.cs`, `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserDirectory.cs`
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/UserManagementTests.cs`

**Interfaces:**
- Produz: `User.LocalLoginEnabled` (bool, default `true`); `UserAccountData` ganha `bool LocalLoginEnabled` e `bool HasPassword`; `UserDetailDto` ganha `bool HasPassword` e `bool LocalLoginEnabled` **no fim do record** (ordem posicional importa para o client gerado).

- [ ] **Passo 1: escrever o teste que falha**

```csharp
// UserManagementTests.cs
[Fact]
public async Task GetUser_ContaRecemCriada_NaoTemSenhaEPermiteLoginLocal()
{
	var (tenantId, userId) = await CriarUsuarioAsync();

	var detail = await GetAsync<UserDetailDto>($"/api/v1/tenants/{tenantId}/users/{userId}");

	detail.HasPassword.Should().BeFalse("a conta nasce sem senha e a pessoa a define pelo convite (ADR-0033)");
	detail.LocalLoginEnabled.Should().BeTrue();
}
```

- [ ] **Passo 2: rodar e ver falhar** — `--filter FullyQualifiedName~UserManagementTests` → não compila.

- [ ] **Passo 3: implementar**

- `User` ganha `public bool LocalLoginEnabled { get; set; } = true;` (coluna `fl_local_login_enabled` pela convention).
- Migration `AddLocalLoginFlag` nos dois engines, com `defaultValue: true` para as linhas existentes.
- `UserDirectory.GetAsync` preenche `HasPassword` com `user.PasswordHash is not null` e `LocalLoginEnabled` com a coluna; `GetUserHandler` repassa ao DTO.

- [ ] **Passo 4: rodar e ver passar** — filtro acima verde.

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): conta sabe se tem senha e se aceita login local (ADR-0033)"
```

---

### Task 5: configuração de e-mail, links e provedores de token

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/CredentialOptions.cs`, `CredentialOptionsValidator.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateCredentialsExtensions.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Api/Program.cs`, `appsettings.Development.json`, `docker-compose.yml`, `.vscode/launch.json`
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Unit/CredentialOptionsTests.cs`

**Interfaces:**
- Produz: `CredentialOptions` com `SectionKey = "SecureGate:Credentials"`, propriedades `InviteLifetimeHours` (72), `ResetLifetimeMinutes` (30), `ForgotPerAccountPerHour` (3), `ForgotPerIpPerHour` (10), `PublicBaseUrl` (lida de `SecureGate:PublicBaseUrl`, exigida fora de Development) e `bool TryValidate(bool isDevelopment, out string? error)`; constantes `CredentialTokenProviders.Invite = "SeccoInvite"` e `.Reset = "SeccoReset"`; `AddSecureGateCredentials(this IServiceCollection, IHostEnvironment)`.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Unit/CredentialOptionsTests.cs
public class CredentialOptionsTests
{
	[Fact]
	public void Options_ForaDeDevelopmentSemBaseUrl_Invalida()
	{
		var options = new CredentialOptions();

		options.TryValidate(isDevelopment: false, out var error).Should().BeFalse();
		error.Should().Contain("SecureGate:PublicBaseUrl");
	}

	[Fact]
	public void Options_BaseUrlRelativa_Invalida()
	{
		var options = new CredentialOptions { PublicBaseUrl = "/conta" };

		options.TryValidate(isDevelopment: false, out _).Should().BeFalse();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(721)]   // teto de 30 dias
	public void Options_ValidadeDeConviteForaDoTeto_Invalida(int hours)
	{
		var options = new CredentialOptions { PublicBaseUrl = "https://id.exemplo", InviteLifetimeHours = hours };

		options.TryValidate(isDevelopment: false, out var error).Should().BeFalse();
		error.Should().Contain("InviteLifetimeHours");
	}

	[Fact]
	public void Options_EmDevelopmentSemBaseUrl_ValidaComPadraoLocal()
	{
		var options = new CredentialOptions();

		options.TryValidate(isDevelopment: true, out _).Should().BeTrue();
	}
}
```

- [ ] **Passo 2: rodar e ver falhar** — `--filter FullyQualifiedName~CredentialOptionsTests`.

- [ ] **Passo 3: implementar as options e a composição**

Teto no código (ADR-0020: valor de configuração governando validade de link é input privilegiado): convite ≤ 720 h, redefinição ≤ 120 min, limites ≥ 1.

```csharp
// src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateCredentialsExtensions.cs
public static IServiceCollection AddSecureGateCredentials(this IServiceCollection services, IHostEnvironment environment)
{
	ArgumentNullException.ThrowIfNull(services);
	ArgumentNullException.ThrowIfNull(environment);

	// PublicBaseUrl mora um nível acima da seção de credenciais; o bind lazy é preservado.
	services.AddOptions<CredentialOptions>()
		.BindConfiguration("SecureGate:Credentials")
		.Configure<IConfiguration>((options, configuration) =>
			options.PublicBaseUrl = configuration["SecureGate:PublicBaseUrl"])
		.ValidateOnStart();
	services.TryAddSingleton<IValidateOptions<CredentialOptions>>(new CredentialOptionsValidator(environment.IsDevelopment()));
	services.TryAddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<CredentialOptions>>().Value);

	// Envio de e-mail: obrigatório fora de Development (ADR-0033 — sem e-mail não há convite
	// nem recuperação, e o admin não pode mais definir senha).
	services.AddSeccoEmail("SecureGate:Email");

	// Validades próprias por provedor, tiradas das options (nada estático).
	services.AddOptions<InviteTokenProviderOptions>().Configure<CredentialOptions>((options, credentials) =>
		options.TokenLifespan = TimeSpan.FromHours(credentials.InviteLifetimeHours));
	services.AddOptions<ResetTokenProviderOptions>().Configure<CredentialOptions>((options, credentials) =>
		options.TokenLifespan = TimeSpan.FromMinutes(credentials.ResetLifetimeMinutes));

	return services;
}
```

Os dois provedores nomeados são subclasses finas do provedor do Identity — `IOptions<T>` é covariante, então a derivada de `DataProtectionTokenProviderOptions` passa direto para a base:

```csharp
// src/SecureGate/Secco.SecureGate.Api/Identity/CredentialTokenProviders.cs
/// <summary>Nomes dos provedores de token de credencial (ADR-0033).</summary>
public static class CredentialTokenProviders
{
	/// <summary>Convite — validade longa (padrão 72 h).</summary>
	public const string Invite = "SeccoInvite";

	/// <summary>Redefinição de senha — validade curta (padrão 30 min).</summary>
	public const string Reset = "SeccoReset";
}

/// <summary>Validade do token de convite, separada da de redefinição.</summary>
public sealed class InviteTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Validade do token de redefinição.</summary>
public sealed class ResetTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Provedor do convite — só existe para ter suas próprias options.</summary>
public sealed class InviteTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<InviteTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<User>> logger)
	: DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);

/// <summary>Provedor da redefinição — idem.</summary>
public sealed class ResetTokenProvider(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<ResetTokenProviderOptions> options,
	ILogger<DataProtectorTokenProvider<User>> logger)
	: DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);
```

No `AddIdentityCore` (Tarefa 3 já mexeu nesse arquivo), encadear depois de `.AddDefaultTokenProviders()`:

```csharp
.AddTokenProvider<InviteTokenProvider>(CredentialTokenProviders.Invite)
.AddTokenProvider<ResetTokenProvider>(CredentialTokenProviders.Reset);
```

`SecureGate:Email` em `appsettings.Development.json` aponta para o MailHog do compose (`Host: localhost`, `Port: 1025`, `UseStartTls: false`, `FromAddress: no-reply@secco.local`), e `SecureGate:PublicBaseUrl` vira `https://localhost:4001`. No `docker-compose.yml`, o serviço do SecureGate ganha as mesmas variáveis (`SecureGate__Email__Host: "mailhog"`), e o `depends_on` inclui o MailHog. No `.vscode/launch.json`, as duas configurações de DEV do SecureGate recebem `SecureGate__PublicBaseUrl` coerente com a porta.

- [ ] **Passo 4: rodar e ver passar** — filtro acima verde + `dotnet build Secco.Platform.slnx -c Release` sem avisos.

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): configuração de e-mail, base pública dos links e provedores de token (ADR-0033)"
```

---

### Task 6: auditoria de credencial (best-effort) e a seção `SecureGate:Audit`

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Application/Credentials/ICredentialAuditor.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/LogStreamCredentialAuditor.cs`, `NoopCredentialAuditor.cs`
- Modificar: `src/SecureGate/Secco.SecureGate.Infrastructure/Elevation/ElevationAuditOptions.cs` (aceita `SecureGate:Audit`, mantém o nome antigo com aviso), `SecureGateInfrastructureExtensions.cs`
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Unit/AuditSectionFallbackTests.cs`

**Interfaces:**
- Produz:

```csharp
/// <summary>Eventos de credencial registrados na trilha (ADR-0033).</summary>
public enum CredentialAuditEvent
{
	PasswordSet,        // convite aceito ou senha trocada pelo dono
	PasswordReset,      // redefinição por link
	InviteSent,         // convite enviado ou reenviado
	RecoveryRequested,  // pedido de "esqueci minha senha" de conta existente
	LinkRejected,       // token inválido, expirado ou já usado
}

/// <summary>
/// Trilha dos eventos de credencial. <b>Best-effort</b> (ADR-0033): falha não impede a operação —
/// recuperar conta e revogar sessão não podem ficar reféns do LogStream. Só a elevação
/// (ADR-0031) segue fail-closed.
/// </summary>
public interface ICredentialAuditor
{
	Task RecordAsync(CredentialAuditEvent auditEvent, Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
```

- [ ] **Passo 1: escrever o teste que falha**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Unit/AuditSectionFallbackTests.cs
public class AuditSectionFallbackTests
{
	[Fact]
	public void Secao_NomeNovo_EhLida()
	{
		var options = Bind(new Dictionary<string, string?>
		{
			["SecureGate:Audit:LogStreamBaseUrl"] = "https://logs.exemplo",
			["SecureGate:Audit:AuthorityUrl"] = "https://id.exemplo",
			["SecureGate:Audit:ClientId"] = "auditor",
			["SecureGate:Audit:ClientSecret"] = "segredo",
		});

		options.IsConfigured.Should().BeTrue();
	}

	[Fact]
	public void Secao_NomeAntigo_ContinuaValendo()
	{
		var options = Bind(new Dictionary<string, string?>
		{
			["SecureGate:ElevationAudit:LogStreamBaseUrl"] = "https://logs.exemplo",
			["SecureGate:ElevationAudit:AuthorityUrl"] = "https://id.exemplo",
			["SecureGate:ElevationAudit:ClientId"] = "auditor",
			["SecureGate:ElevationAudit:ClientSecret"] = "segredo",
		});

		options.IsConfigured.Should().BeTrue("instalação existente não pode quebrar num rename de chave");
	}
}
```

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

- O bind passa a ler `SecureGate:Audit` e, se ela estiver vazia, `SecureGate:ElevationAudit`, registrando um aviso de startup (`ILogger` da composição, `[LoggerMessage]` como o resto do produto) com o nome novo.
- `LogStreamCredentialAuditor` reusa o `HttpClient` nomeado da identidade de auditoria e grava `AuditEntry` com `Action = "credencial." + evento` (`credencial.senha-definida`, `credencial.senha-redefinida`, `credencial.convite-enviado`, `credencial.recuperacao-solicitada`, `credencial.link-recusado`), `ActorType.User`, `ResourceType = "credencial"`, `ResourceId` = id do usuário, **sem token, sem senha, sem e-mail no metadata**.
- Diferença central frente ao auditor de elevação: aqui a exceção é **engolida com log** (best-effort). Registrar `NoopCredentialAuditor` quando a seção não está configurada.

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): trilha best-effort dos eventos de credencial e seção SecureGate:Audit (ADR-0033)"
```

---

### Task 7: convite — `CreateUser` sem senha, reenvio e a tela de definir senha

**Arquivos:**
- Criar: `src/SecureGate/Secco.SecureGate.Application/Credentials/{ICredentialTokens,ICredentialMailer,InviteUserHandler}.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Infrastructure/Credentials/{IdentityCredentialTokens,SeccoEmailCredentialMailer}.cs`
- Criar: `src/SecureGate/Secco.SecureGate.Api/Pages/Shared/_AccountLayout.cshtml`, `Pages/Account/SetPassword.cshtml(.cs)`
- Modificar: `Application/Users/{CreateUserHandler,IUserDirectory}.cs`, `Infrastructure/Users/UserDirectory.cs`, `Api/Requests/UserRequests.cs`, `Api/Endpoints/UserEndpoints.cs`, `Api/Pages/Login.cshtml` (passa a usar o layout), `Program.cs` (`AllowAnonymousToFolder("/Account")`)
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/{CredentialFlowTests,FakeEmailSender}.cs`

**Interfaces:**
- Consome: `ICredentialAuditor` (Tarefa 6), `CredentialOptions` (Tarefa 5), `User.LocalLoginEnabled`/`HasPassword` (Tarefa 4).
- Produz:

```csharp
/// <summary>Geração e validação dos links de credencial (tokens do Identity, ADR-0033).</summary>
public interface ICredentialTokens
{
	Task<string> CreateInviteTokenAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<string> CreateResetTokenAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<CredentialTokenOutcome> SetPasswordAsync(Guid userId, string token, bool invite, string newPassword, CancellationToken cancellationToken = default);
	Task<CredentialTokenOutcome> ChangeOwnPasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

/// <summary>Resultado de consumir um link ou trocar a senha.</summary>
public enum CredentialTokenOutcome { Done, InvalidToken, WeakPassword, NotAllowed }

/// <summary>Envio dos e-mails de credencial. A montagem do link é do adaptador, nunca do caso de uso.</summary>
public interface ICredentialMailer
{
	Task SendInviteAsync(string recipient, Guid userId, string token, CancellationToken cancellationToken = default);
	Task SendResetAsync(string recipient, Guid userId, string token, CancellationToken cancellationToken = default);
	Task SendPasswordChangedNoticeAsync(string recipient, CancellationToken cancellationToken = default);
}

// CreateUserCommand perde Password e ganha LocalLogin:
public sealed record CreateUserCommand(Guid TenantId, string? Email, bool LocalLogin, IReadOnlyList<string>? Roles);
```

- [ ] **Passo 1: escrever os testes que falham**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Integration/CredentialFlowTests.cs
[Fact]
public async Task Convite_CriarUsuario_EnviaLinkEPermiteDefinirSenhaELogar()
{
	var (tenantId, email) = (await CriarTenantAsync(), $"convidado-{Guid.NewGuid():N}@secco.local");

	var created = await AdminClient.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
		new { email, localLogin = true, roles = Array.Empty<string>() });

	created.StatusCode.Should().Be(HttpStatusCode.Created);

	var mail = Factory.SentEmails.Should().ContainSingle().Subject;
	mail.Recipient.Should().Be(email);
	mail.Body.Should().Contain($"{Factory.PublicBaseUrl}/conta/definir-senha?");
	mail.Body.Should().NotContain("senha:", "nenhuma senha viaja por e-mail (ADR-0033)");

	var link = ExtractLink(mail.Body);
	(await SubmitPasswordFormAsync(link, "Nova@Senha123")).StatusCode.Should().Be(HttpStatusCode.Redirect);

	var tokens = await new OidcLoginDriver(Factory, "secco-adminportal", RedirectUri, "Nova@Senha123")
		.LoginAsync(email, "openid profile");
	tokens.AccessToken.Should().NotBeNullOrEmpty();
}

[Fact]
public async Task Convite_LinkUsadoDuasVezes_SegundaVezRecusada()
{
	var link = await ConvidarEObterLinkAsync();
	(await SubmitPasswordFormAsync(link, "Nova@Senha123")).StatusCode.Should().Be(HttpStatusCode.Redirect);

	var again = await SubmitPasswordFormAsync(link, "Outra@Senha123");

	// O token embute o SecurityStamp; definir a senha o trocou e matou os links pendentes.
	(await again.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
}

[Fact]
public async Task CriarUsuario_SemLoginLocal_NaoEnviaConvite()
{
	await CriarUsuarioAsync(localLogin: false);

	Factory.SentEmails.Should().BeEmpty();
}

[Fact]
public async Task ReenviarConvite_ContaQueJaTemSenha_Responde409()
{
	var (tenantId, userId) = await ConvidarEDefinirSenhaAsync();

	var response = await AdminClient.PostAsync($"/api/v1/tenants/{tenantId}/users/{userId}/invite", null);

	response.StatusCode.Should().Be(HttpStatusCode.Conflict);
}

[Fact]
public async Task ReenviarConvite_DuasVezes_MandaDoisLinksESemEstadoDivergente()
{
	var (tenantId, userId) = await CriarUsuarioAsync();

	await AdminClient.PostAsync($"/api/v1/tenants/{tenantId}/users/{userId}/invite", null);
	await AdminClient.PostAsync($"/api/v1/tenants/{tenantId}/users/{userId}/invite", null);

	// ADR-0034: repetir um endpoint cujo propósito é o efeito externo manda outro e-mail,
	// e o pior caso é exatamente esse — nenhum estado diverge, e os dois links morrem juntos.
	Factory.SentEmails.Should().HaveCount(3);
	var links = Factory.SentEmails.TakeLast(2).Select(m => ExtractLink(m.Body)).ToArray();
	(await SubmitPasswordFormAsync(links[0], "Nova@Senha123")).StatusCode.Should().Be(HttpStatusCode.Redirect);
	var segundoLink = await SubmitPasswordFormAsync(links[1], "Outra@Senha123");
	segundoLink.StatusCode.Should().Be(HttpStatusCode.OK);
	(await segundoLink.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
}
```

`FakeEmailSender` implementa `ISeccoEmailSender` guardando `(Recipient, Subject, Body)` numa lista concorrente; a factory de teste o registra em `ConfigureTestServices` e expõe `SentEmails` e `PublicBaseUrl`.

- [ ] **Passo 2: rodar e ver falhar** — `--filter FullyQualifiedName~CredentialFlowTests`.

- [ ] **Passo 3: implementar**

- `CreateUserHandler` perde a validação de senha e passa a mandar `CreateUserData(TenantId, Email, Roles, LocalLogin)` **sem senha**; `UserDirectory.CreateAsync` chama `userManager.CreateAsync(user)` (sem senha) e, quando `LocalLogin` é `true`, o handler pede o convite a `InviteUserHandler`.
- `IdentityCredentialTokens` usa `userManager.GenerateUserTokenAsync(user, CredentialTokenProviders.Invite, "invite")` e `VerifyUserTokenAsync` na volta; `SetPasswordAsync` chama `AddPasswordAsync`/`ResetPasswordAsync` conforme o caso e deixa a política de senha com o Identity (`WeakPassword` vem do `IdentityResult`).
- `SeccoEmailCredentialMailer` monta o link com `CredentialOptions.PublicBaseUrl` + `/conta/definir-senha?userId=...&token=...` (token em `Uri.EscapeDataString`), corpo em texto puro, **sem senha e sem dados pessoais além do necessário**.
- Extrair o CSS do `Login.cshtml` para `Pages/Shared/_AccountLayout.cshtml` e fazer `Login` e as novas páginas usarem `Layout = "_AccountLayout"`. Nenhuma mudança visual no login.
- `SetPassword.cshtml.cs`: `[AllowAnonymous]`, antiforgery do Razor, campos senha + confirmação com teto de 128, `OnPostAsync` chama o handler, e link inválido/expirado renderiza a tela com "pedir outro" apontando para `/conta/esqueci`. Auditar `PasswordSet` ou `LinkRejected`.
- Rotas: `POST /api/v1/tenants/{tenantId}/users/{userId}/invite` (`ResendUserInvite`, 204; 409 se já tem senha ou login local desabilitado; 404 cross-tenant).

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): conta nasce sem senha e a pessoa define pelo convite (ADR-0033)"
```

---

### Task 8: "esqueci minha senha", redefinição e limite de taxa

**Arquivos:**
- Criar: `Application/Credentials/{RequestPasswordResetHandler,ResetPasswordHandler}.cs`
- Criar: `Api/Pages/Account/{Forgot,ResetPassword}.cshtml(.cs)`
- Modificar: `Api/Extensions/SecureGateCredentialsExtensions.cs` (rate limiter), `Program.cs` (`app.UseRateLimiter()`)
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/PasswordRecoveryTests.cs`

**Interfaces:**
- Consome: `ICredentialTokens`, `ICredentialMailer`, `ICredentialAuditor`, `ISessionRevoker.RevokeAllAsync(userId, reason, ct)`.
- Produz: `SessionRevocationReason.PasswordChanged` (valor novo no enum de `Application/Sessions/ISessionRevoker.cs`).

- [ ] **Passo 1: escrever os testes que falham**

```csharp
// tests/SecureGate/Secco.SecureGate.Tests/Integration/PasswordRecoveryTests.cs
[Fact]
public async Task Esqueci_ContaInexistente_RespondeIgualAContaExistente()
{
	var existente = await ResponderFormularioAsync("dev@secco.local");
	var inexistente = await ResponderFormularioAsync($"ninguem-{Guid.NewGuid():N}@secco.local");

	inexistente.StatusCode.Should().Be(existente.StatusCode);
	(await inexistente.Content.ReadAsStringAsync()).Should().Be(await existente.Content.ReadAsStringAsync());
	Factory.SentEmails.Should().ContainSingle(m => m.Recipient == "dev@secco.local");
}

[Fact]
public async Task Esqueci_ContaSoCorporativa_NaoEnviaLinkERespondeIgual()
{
	var email = await CriarContaSemLoginLocalAsync();

	var response = await ResponderFormularioAsync(email);

	response.StatusCode.Should().Be(HttpStatusCode.OK);
	Factory.SentEmails.Should().BeEmpty();
}

[Fact]
public async Task Esqueci_AcimaDoLimitePorConta_RespondeIgualENaoEnvia()
{
	for (var i = 0; i < 3; i++)
	{
		await ResponderFormularioAsync("dev@secco.local");
	}

	var excedente = await ResponderFormularioAsync("dev@secco.local");

	excedente.StatusCode.Should().Be(HttpStatusCode.OK, "exceder o limite não pode ser distinguível (ADR-0020)");
	Factory.SentEmails.Should().HaveCount(3);
}

[Fact]
public async Task Redefinir_PeloLink_EncerraAsSessoesAbertas()
{
	var (email, refreshToken) = await LogarEGuardarRefreshAsync();
	var link = await PedirLinkDeRedefinicaoAsync(email);

	await SubmitResetFormAsync(link, "Nova@Senha123");

	var refreshed = await Driver.RefreshAsync(refreshToken);
	refreshed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "todo evento de senha revoga sessão (ADR-0032/0033)");
}

[Fact]
public async Task Redefinir_ComTokenDeOutraConta_Recusa()
{
	var link = await PedirLinkDeRedefinicaoAsync("dev@secco.local");
	var trocado = TrocarUserIdDoLink(link, outroUserId: await CriarUsuarioAsync());

	var response = await SubmitResetFormAsync(trocado, "Nova@Senha123");
	response.StatusCode.Should().Be(HttpStatusCode.OK);
	(await response.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
}

[Fact]
public async Task Redefinir_DepoisDaValidade_Recusa()
{
	var link = await PedirLinkDeRedefinicaoAsync("dev@secco.local");
	Factory.AdvanceTime(TimeSpan.FromMinutes(31));

	var response = await SubmitResetFormAsync(link, "Nova@Senha123");
	response.StatusCode.Should().Be(HttpStatusCode.OK);
	(await response.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
}

[Fact]
public async Task Redefinir_LogStreamForaDoAr_NaoImpedeARedefinicao()
{
	Factory.FailAudit = true;
	var link = await PedirLinkDeRedefinicaoAsync("dev@secco.local");

	(await SubmitResetFormAsync(link, "Nova@Senha123")).StatusCode.Should().Be(HttpStatusCode.Redirect);
}
```

Para o teste de validade, a factory injeta um `TimeProvider` controlável (`FakeTimeProvider` do `Microsoft.Extensions.TimeProvider.Testing`, já usado no monorepo — conferir com `grep -rn "FakeTimeProvider" tests | head`; se não existir, avançar o relógio **não** é opção e o teste passa a forçar expiração configurando `ResetLifetimeMinutes = 0` numa factory dedicada, conforme o teto mínimo permitido).

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar**

- `/conta/esqueci`: `[AllowAnonymous]`, antiforgery, **resposta única** (mesma view, mesma mensagem) em todos os caminhos — conta inexistente, desativada, tenant inativo, conta só corporativa ou limite excedido.
- O envio acontece **fora do caminho da resposta** (`Task.Run` não; usar `IHostedService`/`Channel` simples interno à página não se justifica — usar o `IBackgroundJobScheduler`? **não**: o SecureGate não tem Hangfire). Forma escolhida: a página **sempre** responde a mesma view e o envio é aguardado com um `Task.WhenAny(envio, Task.Delay(CredentialOptions.ResponseFloorMilliseconds))`, garantindo piso de tempo idêntico entre existir e não existir a conta. O piso (padrão 300 ms) entra em `CredentialOptions`.
- Limite de taxa com o limitador nativo (`AddRateLimiter` + partição por e-mail normalizado e por IP; `OnRejected` devolve **a mesma view**, nunca 429). Lembrar (ADR-0035) que o limite vale por instância.
- `ResetPasswordHandler`: valida token/estado, define a senha, **revoga as sessões** (`SessionRevocationReason.PasswordChanged`), manda e-mail de aviso, audita `PasswordReset` (ou `LinkRejected`).

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): esqueci minha senha e redefinição por link, com limite e resposta genérica (ADR-0033)"
```

---

### Task 9: o usuário troca a própria senha

**Arquivos:**
- Criar: `Application/Credentials/ChangeOwnPasswordHandler.cs`, `Api/Pages/Account/ChangePassword.cshtml(.cs)`
- Modificar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/CredentialFlowTests.cs`

**Interfaces:**
- Consome: `ICredentialTokens.ChangeOwnPasswordAsync`, `ISessionRevoker`, `SignInManager<User>.RefreshSignInAsync`.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task TrocarSenha_SemASenhaAtual_Recusa()
{
	using var browser = await LogarNoCookieAsync("dev@secco.local", DevPassword);

	var response = await SubmitChangeFormAsync(browser, current: "Errada@123", next: "Nova@Senha123");

	(await response.Content.ReadAsStringAsync()).Should().Contain("Senha atual incorreta");
}

[Fact]
public async Task TrocarSenha_PeloDono_MantemASessaoAtualEDerrubaAsOutras()
{
	var (_, refreshDaOutraSessao) = await Driver.LoginAsync("dev@secco.local", "openid profile");
	using var browser = await LogarNoCookieAsync("dev@secco.local", DevPassword);

	var response = await SubmitChangeFormAsync(browser, current: DevPassword, next: "Nova@Senha123");

	response.StatusCode.Should().Be(HttpStatusCode.Redirect);
	// O cookie foi renovado contra o novo stamp: a sessão de quem trocou continua.
	(await browser.GetAsync("/conta/trocar-senha")).StatusCode.Should().Be(HttpStatusCode.OK);
	(await Driver.RefreshAsync(refreshDaOutraSessao)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
	Factory.SentEmails.Should().ContainSingle(m => m.Recipient == "dev@secco.local" && m.Subject.Contains("senha"));
}

[Fact]
public async Task TrocarSenha_ContaSoCorporativa_NaoOferecida()
{
	using var browser = await LogarFederadoAsync();

	(await browser.GetAsync("/conta/trocar-senha")).StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar** — página autenticada pelo cookie do Identity (`[Authorize(AuthenticationSchemes = IdentityConstants.ApplicationScheme)]`), exige a senha atual, troca, revoga as sessões e chama `RefreshSignInAsync` **em seguida**, manda o aviso e audita `PasswordSet`.

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): usuário troca a própria senha exigindo a atual (ADR-0033)"
```

---

### Task 10: redefinição pelo admin e liga/desliga do login local

**Arquivos:**
- Criar: `Application/Credentials/SetLocalLoginHandler.cs`, `Application/Credentials/AdminResetPasswordHandler.cs`
- Modificar: `Api/Endpoints/UserEndpoints.cs`, `Api/Requests/UserRequests.cs`, `Infrastructure/Users/UserDirectory.cs`
- Criar: `tests/SecureGate/Secco.SecureGate.Tests/Integration/LocalLoginToggleTests.cs`

**Interfaces:**
- Produz: `POST /api/v1/tenants/{tenantId}/users/{userId}/password-reset` (`ResetUserPassword`, 204) e `POST .../{userId}/local-login` (`SetUserLocalLogin`, corpo `{ "enabled": bool }`, 204). Ambos sob o escopo `securegate:admin` do grupo existente.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
[Fact]
public async Task RedefinicaoPeloAdmin_EnviaLinkERevogaNaHora()
{
	var (tenantId, userId, email, refreshToken) = await UsuarioLogadoAsync();

	var response = await AdminClient.PostAsync($"/api/v1/tenants/{tenantId}/users/{userId}/password-reset", null);

	response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	Factory.SentEmails.Should().ContainSingle(m => m.Recipient == email);
	(await Driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
}

[Fact]
public async Task DesligarLoginLocal_ApagaASenhaERevoga()
{
	var (tenantId, userId, email, refreshToken) = await UsuarioLogadoAsync();

	await AdminClient.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users/{userId}/local-login", new { enabled = false });

	var detail = await GetAsync<UserDetailDto>($"/api/v1/tenants/{tenantId}/users/{userId}");
	detail.HasPassword.Should().BeFalse();
	detail.LocalLoginEnabled.Should().BeFalse();
	(await Driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
	(await TentarLoginPorSenhaAsync(email, DevPassword)).Should().BeFalse();
}

[Fact]
public async Task DesligarLoginLocal_Repetido_NaoRevogaDeNovoNemEnvia()
{
	var (tenantId, userId, _, _) = await UsuarioLogadoAsync();
	await AdminClient.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users/{userId}/local-login", new { enabled = false });
	var versaoDepoisDaPrimeira = await LerVersaoDeSessaoAsync(userId);

	await AdminClient.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users/{userId}/local-login", new { enabled = false });

	// ADR-0034: efeito colateral só na transição real.
	(await LerVersaoDeSessaoAsync(userId)).Should().Be(versaoDepoisDaPrimeira);
	Factory.SentEmails.Should().BeEmpty();
}

[Fact]
public async Task LigarLoginLocal_EnviaConvite()
{
	var (tenantId, userId, email) = await ContaSoCorporativaAsync();

	await AdminClient.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users/{userId}/local-login", new { enabled = true });

	Factory.SentEmails.Should().ContainSingle(m => m.Recipient == email);
}

[Fact]
public async Task PasswordReset_DeOutroTenant_Responde404()
{
	var (_, userId, _, _) = await UsuarioLogadoAsync();
	var outroTenant = await CriarTenantAsync();

	var response = await AdminClient.PostAsync($"/api/v1/tenants/{outroTenant}/users/{userId}/password-reset", null);

	response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

- [ ] **Passo 2: rodar e ver falhar.**

- [ ] **Passo 3: implementar** — os dois handlers, com `409` para "login local desabilitado" no `password-reset`, `404` para usuário de outro tenant (mesma resposta de inexistente), revogação e auditoria nos pontos certos.

- [ ] **Passo 4: rodar e ver passar.**

- [ ] **Passo 5: commit**

```bash
git commit -am "feat(securegate): redefinição pelo admin e liga/desliga do login local (ADR-0033)"
```

---

### Task 11: contrato, client NSwag e AdminPortal

**Arquivos:**
- Modificar: `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json` (regenerado), `src/SecureGate/Secco.SecureGate.Client/**` (regenerado)
- Modificar: `src/AdminPortal/Secco.AdminPortal/Services/IUserAdminService.cs`, `Components/Pages/TenantManagement.razor`, `Components/Pages/UserManagement.razor`
- Modificar: `tests/AdminPortal/Secco.AdminPortal.Tests/**` conforme os serviços mudarem

- [ ] **Passo 1: regenerar o contrato e o client**

```bash
SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release
dotnet build src/SecureGate/Secco.SecureGate.Client/Secco.SecureGate.Client.csproj -c Release
```

Conferir no diff que `CreateUserRequest` perdeu `password` e ganhou `localLogin`, e que as três rotas novas apareceram.

- [ ] **Passo 2: ajustar o AdminPortal**

- `IUserAdminService.CreateUserAsync(Guid tenantId, string email, bool localLogin, IReadOnlyList<string> roles)` — o parâmetro de senha some.
- `TenantManagement.razor`: o campo "Senha inicial" sai; entra um seletor com duas opções — **"Com senha local (envia convite por e-mail)"** e **"Só login corporativo"**. Ao criar, mensagem: "Convite enviado para {email}." ou "Usuário criado. O acesso é pelo diretório corporativo.".
- `UserManagement.razor`: conforme `HasPassword`/`LocalLoginEnabled`, mostrar **Reenviar convite** (sem senha, login local ligado), **Redefinir senha** (com senha) ou nenhum dos dois (só corporativa), mais o liga/desliga do login local com confirmação, já que desligar apaga a senha e derruba sessões. Botões desabilitados enquanto a requisição está em curso (ADR-0034).
- Erros do client traduzidos pelo `ApiErrorFormatter` existente.

- [ ] **Passo 3: rodar a solution inteira**

`dotnet build Secco.Platform.slnx -c Release && dotnet test Secco.Platform.slnx` → tudo verde (o CI roda a solution; projeto a projeto já deixou bug passar).

- [ ] **Passo 4: commit**

```bash
git commit -am "feat(adminportal): criar usuário por convite e gerir credenciais do usuário (ADR-0033)"
```

---

### Task 12: verificação por mutação, documentação e publicação

**Arquivos:**
- Criar: `C:\...\scratchpad\mutate_credenciais.py` (fora do repositório)
- Modificar: `CHANGELOG.md`, `README.md`, `docs/getting-started.md`, `docs/roadmap.md`, `docs/design-decisions-log.md`, `src/SecureGate/README.md`

- [ ] **Passo 1: bateria de mutação**

Nos moldes de `mutate_sessoes.py`: aplicar uma mutação por vez, rodar o filtro de teste, restaurar e conferir byte a byte. Detectar falha de build por regex `: error [A-Z]+\d+` (mutação que não compila **não** conta como detectada). Invariantes a mutar, com o teste que deve pegar cada uma:

| # | Mutação | Teste esperado |
|---|---|---|
| M1 | link montado a partir do header `Host` em vez de `PublicBaseUrl` | `Convite_CriarUsuario_...` |
| M2 | "esqueci" responde 404 para conta inexistente | `Esqueci_ContaInexistente_RespondeIgual...` |
| M3 | limite de taxa removido | `Esqueci_AcimaDoLimite...` |
| M4 | redefinição não revoga sessões | `Redefinir_PeloLink_EncerraAsSessoes...` |
| M5 | troca da própria senha aceita senha atual errada | `TrocarSenha_SemASenhaAtual_Recusa` |
| M6 | conta só corporativa recebe link | `Esqueci_ContaSoCorporativa_...` |
| M7 | token de convite validado sem conferir o usuário | `Redefinir_ComTokenDeOutraConta_Recusa` |
| M8 | desligar login local não apaga a senha | `DesligarLoginLocal_ApagaASenhaERevoga` |
| M9 | `local-login` repetido revoga de novo | `DesligarLoginLocal_Repetido_...` |
| M10 | auditoria vira fail-closed | `Redefinir_LogStreamForaDoAr_...` |
| M11 | `ResendUserInvite` aceita conta com senha | `ReenviarConvite_ContaQueJaTemSenha_Responde409` |
| M12 | `password-reset` aceita usuário de outro tenant | `PasswordReset_DeOutroTenant_Responde404` |

Qualquer mutação sobrevivente = teste faltando; escrever o teste e repetir.

- [ ] **Passo 2: documentação**

- `CHANGELOG.md`, em "Não publicado": subseções `Secco.SDK.Email` (0.1.0, pacote novo), `Secco.SecureGate.Client` (0.9.0, **quebra** do `CreateUser`), e o que mais a cadeia do MinVer arrastar. Destacar a nova exigência de configuração de e-mail.
- `docs/roadmap.md`: marcar a entrega B com o resumo das decisões.
- `docs/design-decisions-log.md`: as perguntas desta rodada e as alternativas descartadas (envio pelo NotificationHub, tabela própria de tokens, chaves em arquivo).
- `src/SecureGate/README.md`: seção de configuração (`SecureGate:Email`, `SecureGate:PublicBaseUrl`, `SecureGate:Credentials:*`, `SecureGate:Audit`) e o aviso de que **sem e-mail a instalação não sobe fora de Development**.

- [ ] **Passo 3: checklist da skill `secco-platform-standards`** — rodar item a item antes de qualquer push.

- [ ] **Passo 4: publicação (só com autorização explícita do usuário)**

Seguir a skill `secco-platform-release`:

```bash
python scripts/check-release-chain.py sdk-email/v
python scripts/check-release-chain.py securegate-client/v
```

Taguear no HEAD as dependências apontadas, **uma tag por push**, esperando cada workflow concluir (`gh run list --workflow=publish-packages.yml --limit 10`). `Secco.SDK.Email` 0.1.0 é pacote novo: conferir que os três lugares da Tarefa 1 estão no commit publicado, senão a tag não gera artefato. Depois, mover o CHANGELOG para "Publicado" e atualizar `README.md` e `docs/getting-started.md`.

- [ ] **Passo 5: avisar o adotante** — `secco-intranet` consome `Secco.SecureGate.Client`: a quebra do `CreateUser` e a exigência de configuração de e-mail entram no aviso, junto com os pacotes da entrega A que ele ainda não adotou.

---

## Auto-revisão

**Cobertura da spec:** pacote novo (T1), adoção pelo NotificationHub (T2), Data Protection no banco (T3), `fl_local_login_enabled` + `hasPassword`/`localLoginEnabled` (T4), configuração e provedores de token (T5), quatro eventos auditados best-effort e `SecureGate:Audit` (T6), convite e quebra do `CreateUser` (T7), esqueci/redefinir com limite e resposta genérica (T8), troca da própria senha mantendo a sessão (T9), redefinição pelo admin e liga/desliga (T10), contrato + client + AdminPortal (T11), mutação, docs e publicação (T12). Tudo o que a spec lista está coberto.

**Tipos consistentes:** `ISeccoEmailSender.SendAsync` tem a mesma assinatura do `IEmailSender` que substitui; `CreateUserCommand`/`CreateUserData` perdem `Password` e ganham `LocalLogin` nas duas pontas; `CredentialTokenOutcome` é usado por T7, T8 e T9 com os mesmos valores; `SessionRevocationReason.PasswordChanged` nasce em T8 e é usado em T9 e T10.

**Pontos que o executor deve confirmar no código antes de seguir** (não são lacunas do plano, são verificações baratas): existência de `FakeTimeProvider` nos testes (T8 traz o plano B), e o nome exato das colunas que a `SeccoNamingConvention` gera para `DataProtectionKey` (T3 mapeia explicitamente, então o risco é só de nome, não de comportamento).
