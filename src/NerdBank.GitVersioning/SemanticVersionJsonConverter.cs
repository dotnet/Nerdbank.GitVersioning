// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Newtonsoft.Json;

namespace Nerdbank.GitVersioning;

internal class SemanticVersionJsonConverter : JsonConverter
{
    /// <inheritdoc/>
    public override bool CanConvert(Type objectType)
    {
        return typeof(SemanticVersion).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
