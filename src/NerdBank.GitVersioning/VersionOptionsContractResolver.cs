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

            if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.NuGetPackageVersion))
            {
                property.ShouldSerialize = instance => ((VersionOptions)instance).ShouldSerializeNuGetPackageVersion();
            }

            if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.CloudBuild))
            {
                property.ShouldSerialize = instance => ((VersionOptions)instance).ShouldSerializeCloudBuild();
            }

            if (property.DeclaringType == typeof(VersionOptions.CloudBuildOptions) && member.Name == nameof(VersionOptions.CloudBuildOptions.SetVersionVariables))
            {
                property.ShouldSerialize = instance => ((VersionOptions.CloudBuildOptions)instance).ShouldSerializeSetVersionVariables();
            }

            if (property.DeclaringType == typeof(VersionOptions.CloudBuildNumberOptions) && member.Name == nameof(VersionOptions.CloudBuildNumberOptions.IncludeCommitId))
            {
                property.ShouldSerialize = instance => ((VersionOptions.CloudBuildNumberOptions)instance).ShouldSerializeIncludeCommitId();
            }

            if (property.DeclaringType == typeof(VersionOptions.CloudBuildNumberCommitIdOptions) && member.Name == nameof(VersionOptions.CloudBuildNumberCommitIdOptions.When))
            {
                property.ShouldSerialize = instance => ((VersionOptions.CloudBuildNumberCommitIdOptions)instance).ShouldSerializeWhen();
            }

            if (property.DeclaringType == typeof(VersionOptions.CloudBuildNumberCommitIdOptions) && member.Name == nameof(VersionOptions.CloudBuildNumberCommitIdOptions.Where))
            {
                property.ShouldSerialize = instance => ((VersionOptions.CloudBuildNumberCommitIdOptions)instance).ShouldSerializeWhere();
            }

            return property;
        }
    }
}
