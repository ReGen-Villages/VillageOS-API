using System.Text.Json;

namespace vos.CLI
{
    public static class JsonElementExtensions
    {
        public static string GetStringOrDefault(this JsonElement element, string propertyName, string defaultValue = "N/A")
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? defaultValue : defaultValue;
        }

        public static bool GetBoolOrDefault(this JsonElement element, string propertyName, bool defaultValue = false)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.True;
        }

        public static int GetIntOrDefault(this JsonElement element, string propertyName, int defaultValue = 0)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
                ? prop.GetInt32()
                : defaultValue;
        }

        public static int? GetNullableInt(this JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
                ? prop.GetInt32()
                : null;
        }

        public static DateTime? GetNullableDateTime(this JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null
                ? prop.GetDateTime()
                : null;
        }

        public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop : null;
        }

        public static bool HasObjectProperty(this JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Object;
        }

        public static string FormatPropertyValue(this JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => value.GetRawText()
            };
        }
    }
}
