using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Secco.SDK.AspNetCore.SecurityHeaders;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.SecurityHeaders;

public sealed class SeccoCspNonceTagHelperTests
{
	[Theory]
	[InlineData("script")]
	[InlineData("style")]
	public void Troca_o_marcador_pelo_nonce_da_requisicao(string tagName)
	{
		var httpContext = new DefaultHttpContext();
		var feature = new SeccoCspNonceFeature(httpContext);
		httpContext.Features.Set(feature);
		var helper = new SeccoCspNonceTagHelper { ViewContext = new ViewContext { HttpContext = httpContext } };
		var output = new TagHelperOutput(
			tagName,
			[new TagHelperAttribute(SeccoCspNonceTagHelper.AttributeName)],
			(_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

		helper.Process(new TagHelperContext([], new Dictionary<object, object>(), "id"), output);

		output.Attributes.ContainsName(SeccoCspNonceTagHelper.AttributeName).Should().BeFalse();
		output.Attributes["nonce"].Value.Should().Be(feature.Value);
		feature.Used.Should().BeTrue();
	}
}
