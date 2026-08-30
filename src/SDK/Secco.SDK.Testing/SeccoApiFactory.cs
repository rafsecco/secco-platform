using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.Testing;

/// <summary>
/// Base das factories de teste de integração da plataforma (ADR-0012/ADR-0027): sobe a API real
/// no ambiente <c>Testing</c> contra um SQL Server real, emite tokens compatíveis com a
/// configuração de autenticação e monta as chaves de tenancy e de permissões.
/// <para>
/// Produto novo herda daqui e não escreve a própria <c>ApiFactory</c>. A extensão acontece pelos
/// hooks — <see cref="ConfigureTestConfiguration"/>, <see cref="ConfigureTestServices"/> e
/// <see cref="OnInitializedAsync"/>; <c>ConfigureWebHost</c> é selado, e é o que impede o drift
/// entre cópias de voltar por dentro.
/// </para>
/// </summary>
/// <typeparam name="TProgram">Classe <c>Program</c> da API sob teste.</typeparam>
public abstract class SeccoApiFactory<TProgram> : WebApplicationFactory<TProgram>, IAsyncLifetime
	where TProgram : class
{
	/// <summary>Issuer dos tokens de teste.</summary>
	public const string TestIssuer = "secco-tests";

	/// <summary>Role padrão dos tokens de teste — as permissões vêm de <see cref="AddRolePermissions"/>.</summary>
	public const string DefaultTestRole = "test-admin";

	private readonly SeccoSqlServerInstance _sqlServer = new();
	private readonly SemaphoreSlim _migrationLock = new(1, 1);

	/// <summary>
	/// Chave de assinatura ALEATÓRIA por instância (ADR-0020/ADR-0027). Nunca uma constante:
	/// uma chave embutida num pacote publicado seria de conhecimento público, e o validador de
	/// autenticação da plataforma checa presença e comprimento — não tem como saber que uma
	/// chave é notória.
	/// </summary>
	private readonly string _signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

	private bool _migrated;

	/// <summary>
	/// Exposta só para os testes do próprio pacote, que travam a regressão de alguém
	/// reintroduzir uma chave constante. Fica <c>internal</c> para não alargar a superfície
	/// pública sujeita a semver.
	/// </summary>
	internal string SigningKey => _signingKey;

	/// <summary>Audience esperada pela API sob teste (ex.: <c>secco-logstream</c>).</summary>
	protected abstract string Audience { get; }

	/// <summary>
	/// Aplica as migrations do produto. Chamado no máximo uma vez por instância, sob trava —
	/// ver <see cref="EnsureDatabaseMigratedAsync"/>.
	/// </summary>
	/// <param name="services">Provider da aplicação já composta.</param>
	protected abstract Task MigrateAsync(IServiceProvider services);

	/// <summary>
	/// Acrescenta ou remove chaves de configuração de teste. O dicionário já chega com audience,
	/// issuer e chave de assinatura; remover uma chave é intenção explícita (é como a variante
	/// que valida os próprios tokens desliga a chave HS256).
	/// </summary>
	/// <param name="settings">Configuração em memória que será aplicada ao host.</param>
	protected virtual void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
	}

	/// <summary>Substitui serviços da aplicação por dublês de teste (ex.: um sender de e-mail falso).</summary>
	/// <param name="services">Coleção de serviços, já com tudo o que a aplicação registrou.</param>
	protected virtual void ConfigureTestServices(IServiceCollection services)
	{
	}

	/// <summary>
	/// Roda depois que a instância de SQL Server está de pé e antes dos testes — o lugar de
	/// criar bancos que a infraestrutura do produto não cria sozinha.
	/// </summary>
	protected virtual Task OnInitializedAsync() => Task.CompletedTask;

	/// <summary>Connection string de um database desta instância (o nome recebe um sufixo de execução).</summary>
	/// <param name="databaseName">Nome-base do database, sem sufixo.</param>
	public string GetConnectionStringFor(string databaseName) =>
		_sqlServer.GetConnectionStringFor(databaseName);

	/// <summary>Cria um database vazio, para infraestrutura que não cria o próprio banco (ex.: Hangfire).</summary>
	/// <param name="databaseName">Nome-base do database, sem sufixo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public Task CreateDatabaseAsync(string databaseName, CancellationToken cancellationToken = default) =>
		_sqlServer.CreateDatabaseAsync(databaseName, cancellationToken);

	/// <summary>Aplica as migrations do produto uma única vez por instância, mesmo sob chamadas concorrentes.</summary>
	public async Task EnsureDatabaseMigratedAsync()
	{
		await _migrationLock.WaitAsync();

		try
		{
			if (!_migrated)
			{
				await MigrateAsync(Services);
				_migrated = true;
			}
		}
		finally
		{
			_migrationLock.Release();
		}
	}

	/// <summary>
	/// Token de usuário/máquina com tenant e role (claims curtas, ADR-0007). As permissões do
	/// role saem da configuração montada por <see cref="AddRolePermissions"/> (ADR-0021).
	/// </summary>
	/// <param name="tenantId">Tenant da claim <c>tenant_id</c>.</param>
	/// <param name="subject">Claim <c>sub</c>.</param>
	/// <param name="role">Claim <c>role</c>.</param>
	public string CreateToken(Guid tenantId, string subject = "test-user", string role = DefaultTestRole) =>
		CreateToken(new Dictionary<string, object>(StringComparer.Ordinal)
		{
			[SeccoClaims.Subject] = subject,
			[SeccoClaims.TenantId] = tenantId.ToString(),
			[SeccoClaims.Role] = role,
		});

	/// <summary>
	/// Token com scopes e sem tenant — o formato de client credentials que exercita a
	/// autorização por scope sem passar pelo fluxo OIDC completo. Nome distinto de
	/// <see cref="CreateToken(Guid, string, string)"/> por design: um overload posicional
	/// confundiria scope com role.
	/// </summary>
	/// <param name="scopes">Scopes concedidos (separados por espaço na claim, RFC 8693).</param>
	public string CreateTokenWithScopes(params string[] scopes)
	{
		ArgumentNullException.ThrowIfNull(scopes);

		return CreateToken(new Dictionary<string, object>(StringComparer.Ordinal)
		{
			[SeccoClaims.Subject] = DefaultTestRole,
			[SeccoClaims.Scope] = string.Join(' ', scopes),
		});
	}

	/// <summary>Registra um tenant no catálogo por configuração do SDK (ADR-0005).</summary>
	/// <param name="settings">Configuração em memória sendo montada.</param>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="connectionString">Connection string do banco daquele tenant.</param>
	protected static void AddTenant(IDictionary<string, string?> settings, Guid tenantId, string connectionString)
	{
		ArgumentNullException.ThrowIfNull(settings);

		settings[$"Secco:Tenancy:Tenants:{tenantId}:ConnectionString"] = connectionString;
	}

	/// <summary>
	/// Concede permissões a um role no resolver por configuração (ADR-0021). Emite os índices —
	/// digitá-los à mão gera chave duplicada ao inserir no meio da lista, e a última vence em
	/// silêncio.
	/// </summary>
	/// <param name="settings">Configuração em memória sendo montada.</param>
	/// <param name="role">Role que recebe as permissões.</param>
	/// <param name="permissions">Permissões no formato <c>recurso:acao</c>.</param>
	protected static void AddRolePermissions(
		IDictionary<string, string?> settings,
		string role,
		params string[] permissions)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(permissions);

		for (var index = 0; index < permissions.Length; index++)
		{
			var key = string.Create(
				CultureInfo.InvariantCulture,
				$"Secco:Authorization:Roles:{role}:Permissions:{index}");

			settings[key] = permissions[index];
		}
	}

	/// <inheritdoc />
	public async Task InitializeAsync()
	{
		await _sqlServer.StartAsync();
		await OnInitializedAsync();
	}

	/// <summary>
	/// Fixa o ambiente, monta a configuração base e chama os hooks. Selado de propósito
	/// (ADR-0027): é o que impede uma cópia divergente de reaparecer por herança.
	/// </summary>
	/// <param name="builder">Builder do host de teste.</param>
	protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.UseEnvironment("Testing");

		var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["Secco:Authentication:Audience"] = Audience,
			["Secco:Authentication:Issuer"] = TestIssuer,
			["Secco:Authentication:DevelopmentSigningKey"] = _signingKey,
		};

		ConfigureTestConfiguration(settings);

		builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
		builder.ConfigureTestServices(services => ConfigureTestServices(services));
	}

	async Task IAsyncLifetime.DisposeAsync()
	{
		await base.DisposeAsync();
		await _sqlServer.DisposeAsync();
		_migrationLock.Dispose();
	}

	private string CreateToken(IDictionary<string, object> claims) =>
		new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
		{
			Issuer = TestIssuer,
			Audience = Audience,
			Claims = claims,
			Expires = DateTime.UtcNow.AddMinutes(10),
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)),
				SecurityAlgorithms.HmacSha256),
		});
}
