using System.Text.Json;
using System.Text.Json.Serialization;
using RenderLab.Functional;

namespace RenderLab.Project;

/// <summary>
/// JSON converter for <see cref="Optional{T}"/>. <c>None</c> writes <c>null</c>;
/// <c>Some(v)</c> writes the inner value. The on-disk shape stays identical to
/// a nullable reference, so swapping a domain field from <c>T?</c> to
/// <c>Optional&lt;T&gt;</c> requires no migration.
/// </summary>
public sealed class OptionalJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var inner = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalConverter<>).MakeGenericType(inner);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class OptionalConverter<T> : JsonConverter<Optional<T>> where T : notnull
    {
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return Optional<T>.None;
            var value = JsonSerializer.Deserialize<T>(ref reader, options);
            return value is null ? Optional<T>.None : Optional<T>.Some(value);
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            value.Match<int>(
                some: v => { JsonSerializer.Serialize(writer, v, options); return 0; },
                none: () => { writer.WriteNullValue(); return 0; });
        }
    }
}
