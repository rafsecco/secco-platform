using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Secco.SecureGate.Tests.Architecture;

/// <summary>
/// Teste de arquitetura do isolamento de tenant (ADR-0041): lê o código-fonte de
/// <c>src/SecureGate</c> como texto. Nenhum <c>IgnoreQueryFilters</c> fora de <c>Migrations/</c>, e cada
/// chamada de <c>EnterPlatform(</c> tem uma linha na allowlist versionada (e vice-versa).
/// <para>
/// <b>O que conta como chamada:</b> toda ocorrência do token <c>EnterPlatform(</c> em linha de código de
/// <c>src/SecureGate</c> — inclusive a forma <c>RequirePlatformScope(scope =&gt; scope.EnterPlatform("motivo"))</c>.
/// Não contam: a declaração na interface e a implementação (com o <c>[LoggerMessage]</c>) em
/// <see cref="DeclaringFiles"/>, linhas inteiras de comentário (<c>//</c>, <c>///</c>) e <c>LogEnterPlatform(</c>.
/// A comparação é por <b>arquivo + número de chamadas</b>, estável a mudança de número de linha.
/// </para>
/// </summary>
public sealed partial class TenantIsolationArchitectureTests
{
	private const string SourceRoot = "src/SecureGate";

	private const string AllowlistPath = "tests/SecureGate/Secco.SecureGate.Tests/Architecture/platform-scope-allowlist.txt";

	// A declaração e a implementação do próprio método contêm "EnterPlatform(" sem serem chamadas.
	private static readonly string[] DeclaringFiles =
	[
		"src/SecureGate/Secco.SecureGate.Application/Tenancy/ITenantAccessScope.cs",
		"src/SecureGate/Secco.SecureGate.Infrastructure/Tenancy/TenantAccessScope.cs",
	];

	[GeneratedRegex(@"\bEnterPlatform\(")]
	private static partial Regex EnterPlatformCall();

	[Fact]
	public void NenhumIgnoreQueryFilters_ForaDeMigrations()
	{
		var offenders = SourceFiles()
			.Where(f => !f.Relative.Contains("/Migrations/", StringComparison.Ordinal))
			.Where(f => CodeLines(f.Path).Any(l => l.Contains("IgnoreQueryFilters", StringComparison.Ordinal)))
			.Select(f => f.Relative)
			.ToList();

		offenders.Should().BeEmpty("IgnoreQueryFilters é proibido: o modo plataforma explícito é a única saída do filtro (ADR-0041)");
	}

	[Fact]
	public void CadaEnterPlatform_TemUmaLinhaNaAllowlist_ECadaLinhaTemUmaChamada()
	{
		var actual = SourceFiles()
			.Where(f => !DeclaringFiles.Contains(f.Relative))
			.Select(f => (f.Relative, Count: CodeLines(f.Path).Sum(l => EnterPlatformCall().Count(l))))
			.Where(x => x.Count > 0)
			.ToDictionary(x => x.Relative, x => x.Count);

		var allowed = ReadAllowlist()
			.GroupBy(e => e.File)
			.ToDictionary(g => g.Key, g => g.Count());

		var problems = new List<string>();

		foreach (var file in actual.Keys.Union(allowed.Keys).OrderBy(k => k, StringComparer.Ordinal))
		{
			var calls = actual.GetValueOrDefault(file);
			var lines = allowed.GetValueOrDefault(file);

			if (calls != lines)
			{
				problems.Add($"{file}: {calls} chamada(s) de EnterPlatform, {lines} linha(s) na allowlist");
			}
		}

		problems.Should().BeEmpty("a allowlist tem uma linha por chamada, sem sobras (ADR-0041)");
	}

	[Fact]
	public void Allowlist_TemMotivoEmTodaLinha()
	{
		ReadAllowlist().Should().OnlyContain(e => e.Reason.Length > 0, "toda entrada em modo plataforma precisa de motivo");
	}

	private static List<(string File, string Reason)> ReadAllowlist()
	{
		var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), AllowlistPath));
		var entries = new List<(string, string)>();

		foreach (var line in lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
		{
			var parts = line.Split('|', 2);
			entries.Add((parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty));
		}

		return entries;
	}

	/// <summary>Linhas de código, sem as que são só comentário (<c>//</c>, <c>///</c>).</summary>
	private static IEnumerable<string> CodeLines(string path) =>
		File.ReadLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));

	private static IEnumerable<(string Path, string Relative)> SourceFiles()
	{
		var root = FindRepositoryRoot();

		return Directory
			.EnumerateFiles(Path.Combine(root, SourceRoot), "*.cs", SearchOption.AllDirectories)
			.Select(p => (Path: p, Relative: Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/')))
			.Where(f => !f.Relative.Contains("/obj/", StringComparison.Ordinal)
				&& !f.Relative.Contains("/bin/", StringComparison.Ordinal));
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Secco.Platform.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName
			?? throw new InvalidOperationException("Raiz do repositório (Secco.Platform.slnx) não encontrada.");
	}
}
