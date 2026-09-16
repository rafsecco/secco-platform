using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// As APIs de role do Identity localizam perfil por nome GLOBAL. Aqui o nome só é único por tenant:
/// usá-las alcançaria o perfil homônimo de outro tenant. O teste lê o código do SecureGate e falha ao
/// encontrá-las — sem biblioteca de arquitetura nova.
/// </summary>
public partial class IdentityRoleApiGuardTests
{
	[GeneratedRegex(@"\b(AddToRolesAsync|AddToRoleAsync|RemoveFromRolesAsync|RemoveFromRoleAsync|IsInRoleAsync|GetUsersInRoleAsync)\s*\(|\bRoleManager\s*<")]
	private static partial Regex ForbiddenCall();

	[Fact]
	public void SecureGate_NaoUsaApisDeRoleQueIgnoramOTenant()
	{
		var root = FindRepositoryRoot();

		var offenders = Directory
			.EnumerateFiles(Path.Combine(root, "src", "SecureGate"), "*.cs", SearchOption.AllDirectories)
			.Where(path => !IsGenerated(path))
			.SelectMany(path => File.ReadLines(path).Select((line, index) => (Path: path, Line: line, Number: index + 1)))
			.Where(entry => ForbiddenCall().IsMatch(entry.Line))
			.Select(entry => $"{Path.GetRelativePath(root, entry.Path)}:{entry.Number}")
			.ToList();

		offenders.Should().BeEmpty("perfil é localizado sempre por (tenant, nome normalizado)");
	}

	[Theory]
	[InlineData("await userManager.AddToRoleAsync(user, \"admin\");")]
	[InlineData("await userManager.RemoveFromRolesAsync(user, roles);")]
	[InlineData("if (await userManager.IsInRoleAsync (user, name))")]
	[InlineData("RoleManager<Role> roleManager")]
	public void Guarda_ReconheceChamadasProibidas(string line) => ForbiddenCall().IsMatch(line).Should().BeTrue();

	[Theory]
	[InlineData("/// não usa <c>UserManager.AddToRoleAsync</c> por nome")]
	[InlineData("var roles = await userManager.GetRolesAsync(user);")]
	public void Guarda_NaoAcusaComentarioNemLeituraPorUsuario(string line) => ForbiddenCall().IsMatch(line).Should().BeFalse();

	private static bool IsGenerated(string path)
	{
		var separator = Path.DirectorySeparatorChar;

		return path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
			|| path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
			|| path.Contains($"{separator}Migrations", StringComparison.Ordinal);
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
