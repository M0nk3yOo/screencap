using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Screencap.Core.Imaging;

namespace Screencap.Core.Annotations;

/// <summary>Stores colors as "#AARRGGBB" so session files stay readable.</summary>
public sealed class ArgbColorJsonConverter : JsonConverter<ArgbColor>
{
    public override ArgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? throw new JsonException("Color expected");
        return ArgbColor.FromUInt(uint.Parse(s.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public override void Write(Utf8JsonWriter writer, ArgbColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(string.Create(CultureInfo.InvariantCulture, $"#{value.Value:X8}"));
}
