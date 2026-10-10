using System.Reflection;
using FluentAssertions;
using Secco.SampleService.Application;
using Xunit;

namespace Secco.SampleService.Tests.Unit;

/// <summary>
/// O catálogo publicado (ADR-0039) precisa conter toda constante de <see cref="SampleServicePermissions"/>:
/// permissão nova sem entrada no <c>Catalog</c> seria recusada pelo SecureGate ao atribuir a um perfil.
/// </summary>
public class PermissionCatalogCompletenessTests
{
	[Fact]
	public void Catalog_ContemTodaConstanteDePermissao()
	{
		var constants = typeof(SampleServicePermissions).GetNestedTypes()
			.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
			.Where(field => field.IsLiteral && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue()!)
			.ToHashSet(StringComparer.Ordinal);

		SampleServicePermissions.Catalog.Select(p => p.Name).Should().BeEquivalentTo(constants);
		SampleServicePermissions.Catalog.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Description));
	}
}
