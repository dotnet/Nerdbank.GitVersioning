namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;

    internal class SemanticVersionJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(SemanticVersion).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (objectType.IsEquivalentTo(typeof(SemanticVersion)) && reader.Value is string)
            {
                SemanticVersion value;
                if (SemanticVersion.TryParse((string)reader.Value, out value))
                {
                    return value;
                }
            }

            throw new NotSupportedException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var version = value as SemanticVersion;
            if (version != null)
            {
                writer.WriteValue(version.ToString());
                return;
            }

            throw new NotSupportedException();
        }
    }
}
