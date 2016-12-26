namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    internal class AssemblyVersionOptionsConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(VersionOptions.AssemblyVersionOptions).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (objectType.IsEquivalentTo(typeof(VersionOptions.AssemblyVersionOptions)))
            {
                if (reader.Value is string)
                {
                    Version value;
                    if (Version.TryParse((string)reader.Value, out value))
                    {
                        return new VersionOptions.AssemblyVersionOptions(value);
                    }
                }
                else if (reader.TokenType == JsonToken.StartObject)
                {
                    // Temporarily remove ourselves from the serializer so we don't recurse infinitely.
                    serializer.Converters.Remove(this);
                    var result = serializer.Deserialize<VersionOptions.AssemblyVersionOptions>(reader);
                    serializer.Converters.Add(this);
                    return result;
                }
            }

            throw new NotSupportedException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var data = value as VersionOptions.AssemblyVersionOptions;
            if (data != null)
            {
                if (data.Precision == VersionOptions.VersionPrecision.Minor)
                {
                    serializer.Serialize(writer, data.Version);
                    return;
                }
                else
                {
                    // Temporarily remove ourselves from the serializer so we don't recurse infinitely.
                    serializer.Converters.Remove(this);
                    serializer.Serialize(writer, data);
                    serializer.Converters.Add(this);
                    return;
                }
            }

            throw new NotSupportedException();
        }
    }
}
