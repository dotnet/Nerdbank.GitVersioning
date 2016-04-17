namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    internal class VersionOptionsContractResolver : CamelCasePropertyNamesContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.AssemblyVersion))
            {
                property.ShouldSerialize = instance => ((VersionOptions)instance).ShouldSerializeAssemblyVersion();
            }

            if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.CloudBuild))
            {
                property.ShouldSerialize = instance => ((VersionOptions)instance).ShouldSerializeCloudBuild();
            }

            if (property.DeclaringType == typeof(VersionOptions.CloudBuildOptions) && member.Name == nameof(VersionOptions.CloudBuildOptions.SetVersionVariables))
            {
                property.ShouldSerialize = instance => ((VersionOptions.CloudBuildOptions)instance).ShouldSerializeSetVersionVariables();
            }

            return property;
        }
    }
}
