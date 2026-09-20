using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morita.LP.Razor.Models;

[JsonConverter(typeof(OnlinePaymentMethodJsonConverter))]
public enum OnlinePaymentMethod
{
    Pix,
    Card
}

public sealed class OnlinePaymentMethodJsonConverter : JsonConverter<OnlinePaymentMethod>
{
    public override OnlinePaymentMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Invalid online payment method.");
        }

        if (StorefrontOnlinePayments.TryParse(reader.GetString(), out var method))
        {
            return method;
        }

        throw new JsonException("Invalid online payment method.");
    }

    public override void Write(Utf8JsonWriter writer, OnlinePaymentMethod value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToWireValue());
}
