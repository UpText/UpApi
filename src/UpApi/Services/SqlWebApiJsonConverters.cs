using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpApi.Services;

internal sealed class UpStringConverter : JsonConverter<string>
{
    private static readonly JsonConverter<string> DefaultConverter =
        (JsonConverter<string>)JsonSerializerOptions.Default.GetConverter(typeof(string));

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DefaultConverter.Read(ref reader, typeToConvert, options) ?? string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (value.Length > 0 && (value[0] == '{' || value[0] == '['))
        {
            try
            {
                using var json = JsonDocument.Parse(value);
                json.WriteTo(writer);
                return;
            }
            catch (JsonException)
            {
            }
        }

        writer.WriteStringValue(value);
    }
}

internal sealed class DbNullConverter : JsonConverter<DBNull>
{
    public override DBNull Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null
            ? DBNull.Value
            : throw new JsonException("Cannot deserialize non-null value to DBNull.");
    }

    public override void Write(Utf8JsonWriter writer, DBNull value, JsonSerializerOptions options)
    {
        writer.WriteNullValue();
    }
}

internal sealed class DateOnlyConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateOnly.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }
}
