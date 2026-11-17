using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.Service.Shared.Subscriptions;

// The broker takes a field of this type as one value or as a list, so it is sent in the form the caller wrote.
[CollectionBuilder(typeof(OneOrMany), nameof(OneOrMany.Create))]
[JsonConverter(typeof(OneOrManyConverter))]
public sealed class OneOrMany<T> : IEnumerable<T>
{
    private readonly T[] _items;

    internal OneOrMany(T[] items, bool isList)
    {
        _items = items;
        IsList = isList;
    }

    public bool IsList { get; }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator OneOrMany<T>(T one) => new([one], isList: false);
}

public static class OneOrMany
{
    public static OneOrMany<T> Create<T>(ReadOnlySpan<T> items) => new(items.ToArray(), isList: true);
}

internal sealed class OneOrManyConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(OneOrMany<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(OfElement<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class OfElement<T> : JsonConverter<OneOrMany<T>>
    {
        public override OneOrMany<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.StartArray
                ? new OneOrMany<T>(JsonSerializer.Deserialize<T[]>(ref reader, options)!, isList: true)
                : JsonSerializer.Deserialize<T>(ref reader, options)!;

        public override void Write(Utf8JsonWriter writer, OneOrMany<T> value, JsonSerializerOptions options)
        {
            if (value.IsList)
                JsonSerializer.Serialize(writer, value.ToArray(), options);
            else
                JsonSerializer.Serialize(writer, value.Single(), options);
        }
    }
}
