using System.Text.Json;
using System.Text.Json.Serialization;
using Screencap.Core.Imaging;

namespace Screencap.Core.Annotations;

/// <summary>Embeds images as base64 PNG, so a session stays a single self-contained manifest.</summary>
public sealed class PixelBufferJsonConverter : JsonConverter<PixelBuffer>
{
    public override PixelBuffer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        PixelBuffer.Decode(reader.GetBytesFromBase64());

    public override void Write(Utf8JsonWriter writer, PixelBuffer value, JsonSerializerOptions options) =>
        writer.WriteBase64StringValue(value.EncodePng());
}
