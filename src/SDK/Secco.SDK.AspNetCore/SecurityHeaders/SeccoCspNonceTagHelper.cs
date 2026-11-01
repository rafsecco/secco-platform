using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Secco.SDK.AspNetCore.SecurityHeaders;

/// <summary>
/// Troca <c>secco-nonce</c> pelo nonce da CSP da requisição em <c>&lt;script&gt;</c> e <c>&lt;style&gt;</c>
/// inline (ADR-0057). Registrar com <c>@addTagHelper *, Secco.SDK.AspNetCore</c> no <c>_ViewImports</c>.
/// </summary>
[HtmlTargetElement("script", Attributes = AttributeName)]
[HtmlTargetElement("style", Attributes = AttributeName)]
public sealed class SeccoCspNonceTagHelper : TagHelper
{
	/// <summary>Atributo que marca o elemento para receber o nonce.</summary>
	public const string AttributeName = "secco-nonce";

	/// <summary>Contexto da view, preenchido pelo Razor.</summary>
	[ViewContext]
	[HtmlAttributeNotBound]
	public ViewContext ViewContext { get; set; } = null!;

	/// <inheritdoc />
	public override void Process(TagHelperContext context, TagHelperOutput output)
	{
		ArgumentNullException.ThrowIfNull(output);

		output.Attributes.RemoveAll(AttributeName);
		output.Attributes.SetAttribute("nonce", ViewContext.HttpContext.GetSeccoCspNonce());
	}
}
