using System.Text.Json;

namespace Morita.LP.Razor.Models;

public static class PickupAddressParser
{
    public static CheckoutAddress? Parse(JsonElement value)
    {
        try
        {
            using var document = value.ValueKind == JsonValueKind.String ? JsonDocument.Parse(value.GetString() ?? "") : null;
            if (document is not null) value = document.RootElement.Clone();
            if (value.ValueKind != JsonValueKind.Object) return null;
            string? Get(string name) => value.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) is { Value.ValueKind: JsonValueKind.String } match ? match.Value.GetString() : null;
            var street = Get("street"); var number = Get("number"); var neighborhood = Get("neighborhood"); var city = Get("city"); var state = Get("state"); var postal = Get("postalCode");
            return string.IsNullOrWhiteSpace(street) || street.Length > 160 || string.IsNullOrWhiteSpace(number) || number.Length > 40 || string.IsNullOrWhiteSpace(neighborhood) || neighborhood.Length > 120 || string.IsNullOrWhiteSpace(city) || city.Length > 120 || string.IsNullOrWhiteSpace(state) || state.Length > 40 || string.IsNullOrWhiteSpace(postal) || postal.Length > 30 || Get("complement")?.Length > 160 ? null : new CheckoutAddress { Street = street.Trim(), Number = number.Trim(), Complement = Get("complement")?.Trim(), Neighborhood = neighborhood.Trim(), City = city.Trim(), State = state.Trim(), PostalCode = postal.Trim() };
        } catch (JsonException) { return null; }
    }
}
