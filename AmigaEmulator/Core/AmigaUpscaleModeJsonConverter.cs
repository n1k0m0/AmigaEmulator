using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmigaEmulator.Core
{
    /// <summary>Reads retired upscale settings without discarding the rest of the user's profile.</summary>
    internal sealed class AmigaUpscaleModeJsonConverter : JsonConverter<AmigaUpscaleMode>
    {
        public override AmigaUpscaleMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string value = reader.GetString();
                if (string.Equals(value, "Xbrz", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "Clean4", StringComparison.OrdinalIgnoreCase))
                    return AmigaUpscaleMode.Hq4x;
                if (Enum.TryParse(value, true, out AmigaUpscaleMode mode)) return Normalize((int)mode);
            }
            else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
                return Normalize(value);
            else
                reader.Skip();
            return AmigaUpscaleMode.None;
        }

        public override void Write(Utf8JsonWriter writer, AmigaUpscaleMode value, JsonSerializerOptions options) =>
            writer.WriteStringValue(AmigaOverlaySettingCatalog.GetLabel(Normalize((int)value)));

        private static AmigaUpscaleMode Normalize(int value) => value switch
        {
            >= 0 and <= 5 => (AmigaUpscaleMode)value,
            6 or 7 => AmigaUpscaleMode.Hq4x,
            _ => AmigaUpscaleMode.None
        };
    }
}
