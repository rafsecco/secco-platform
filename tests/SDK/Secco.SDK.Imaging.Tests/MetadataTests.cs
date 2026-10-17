using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.SDK.Imaging.Internal;
using Xunit;

namespace Secco.SDK.Imaging.Tests;

public sealed class MetadataTests
{
	private const string Marker = "SECCO-GPS-MARKER";

	private static async Task<byte[]> Process(ImageOutputFormat format)
	{
		var processor = new SeccoImageProcessor(
			new SkiaImageCodec(NullLogger<SkiaImageCodec>.Instance),
			Options.Create(new SeccoImagingOptions()),
			NullLogger<SeccoImageProcessor>.Instance);
		var input = TestImages.JpegWithExif(200, 100, 6, Marker);

		// Sanidade do teste: a entrada carrega mesmo o marcador e um APP1.
		Encoding.ASCII.GetString(input).Should().Contain(Marker);

		var request = new ImageProcessingRequest([new ImageVariant("v", 512)], Format: format);
		var result = await processor.ProcessAsync(new MemoryStream(input), request);
		result.IsSuccess.Should().BeTrue();
		return result.Value.Variants["v"].Content;
	}

	[Fact]
	public async Task Saida_jpeg_sem_app1_app2()
	{
		var output = await Process(ImageOutputFormat.Jpeg);

		Encoding.ASCII.GetString(output).Should().NotContain(Marker);

		// Percorre os segmentos até o SOS; dali em diante são dados de entropia.
		var offset = 2;

		while (offset + 4 <= output.Length && output[offset] == 0xFF)
		{
			var marker = output[offset + 1];
			marker.Should().NotBe(0xE1, "APP1 carrega EXIF/XMP");
			marker.Should().NotBe(0xE2, "APP2 carrega o perfil ICC");

			if (marker == 0xDA)
			{
				break;
			}

			offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(offset + 2));
		}
	}

	[Fact]
	public async Task Saida_webp_sem_chunks_de_metadado()
	{
		var output = await Process(ImageOutputFormat.WebP);

		Encoding.ASCII.GetString(output).Should().NotContain(Marker);

		var offset = 12;
		var chunks = new List<string>();

		while (offset + 8 <= output.Length)
		{
			chunks.Add(Encoding.ASCII.GetString(output, offset, 4));
			var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset + 4));
			offset += 8 + size + (size & 1);
		}

		chunks.Should().NotContain(["EXIF", "XMP ", "ICCP"]);
	}
}
