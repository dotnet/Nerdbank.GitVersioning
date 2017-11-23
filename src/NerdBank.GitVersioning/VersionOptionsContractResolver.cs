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
        internal static readonly IContractResolver IncludeDefaultsContractResolver = new VersionOptionsContractResolver();
        internal static readonly IContractResolver ExcludeDefaultsContractResolver = new ExcludeDefaults();

        protected VersionOptionsContractResolver()
        {
        }

        private class ExcludeDefaults : VersionOptionsContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                JsonProperty property = base.CreateProperty(member, memberSerialization);

                if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.AssemblyVersion))
                {
                    property.ShouldSerialize = instance => !((VersionOptions)instance).AssemblyVersionOrDefault.IsDefault;
                }

                if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.BuildNumberOffset))
                {
                    property.ShouldSerialize = instance => ((VersionOptions)instance).BuildNumberOffsetOrDefault != 0;
                }

                if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.NuGetPackageVersion))
                {
                    property.ShouldSerialize = instance => !((VersionOptions)instance).NuGetPackageVersionOrDefault.IsDefault;
                }

                if (property.DeclaringType == typeof(VersionOptions) && member.Name == nameof(VersionOptions.CloudBuild))
                {
                    property.ShouldSerialize = instance => !((VersionOptions)instance).CloudBuildOrDefault.IsDefault;
                }

                if (property.DeclaringType == typeof(VersionOptions.CloudBuildOptions) && member.Name == nameof(VersionOptions.CloudBuildOptions.SetVersionVariables))
                {
                    property.ShouldSerialize = instance => ((VersionOptions.CloudBuildOptions)instance).SetVersionVariablesOrDefault != VersionOptions.CloudBuildOptions.DefaultInstance.SetVersionVariables.Value;
                }

                if (property.DeclaringType == typeof(VersionOptions.CloudBuildNumberOptions) && member.Name == nameof(VersionOptions.CloudBuildNumberOptions.IncludeCommitId))
                {
                    property.ShouldSerialize = instance => !((VersionOptions.CloudBuildNumberOptions)instance).IncludeCommitIdOrDefault.IsDefault;
                }

                if (property.DeclaringType == typeof(VersionOptions.CloudBuildNumberCommitIdOptions) && member.Name == nameof(VersionOptions.CloudBuildNumberCommitIdOptions.When))
                {
                    property.ShouldSerialize = instance => ((VersionOptions.CloudBuildNumberCommitIdOptions)instance).WhenOrDefault != VersionOptions.CloudBuildNumberCommitIdOptions.DefaultInstance.When.Value;
                }

                if (property.DeclaringType == typeof(VersionOptions.CloudBuildNumberCommitIdOptions) && member.Name == nameof(VersionOptions.CloudBuildNumberCommitIdOptions.Where))
                {
                    property.ShouldSerialize = instance => ((VersionOptions.CloudBuildNumberCommitIdOptions)instance).WhereOrDefault != VersionOptions.CloudBuildNumberCommitIdOptions.DefaultInstance.Where.Value;
                }

                return property;
            }
        }
    }
}
