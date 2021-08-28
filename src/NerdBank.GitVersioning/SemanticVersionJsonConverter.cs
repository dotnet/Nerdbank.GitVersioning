namespace Nerdbank.GitVersioning
{
    using System;
    using System.Reflection;
    using Newtonsoft.Json;

    internal class SemanticVersionJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(SemanticVersion).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (objectType.Equals(typeof(SemanticVersion)) && reader.Value is string)
            {
                SemanticVersion value;
                if (SemanticVersion.TryParse((string)reader.Value, out value))
                {
                    return value;
                }
                else
                {
                    throw new FormatException($"The value \"{reader.Value}\" is not a valid semantic version.");
                }
            }

            throw new NotSupportedException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var version = value as SemanticVersion;
            if (version is not null)
            {
                writer.WriteValue(version.ToString());
                return;
            }

            throw new NotSupportedException();
        }
    }
}
