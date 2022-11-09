// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
#if NETCOREAPP
using System.Runtime.Loader;
using Nerdbank.GitVersioning;
#endif
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MSBuildExtensionTask
{
    public abstract class ContextAwareTask : Microsoft.Build.Utilities.Task
    {
        protected virtual string ManagedDllDirectory => Path.GetDirectoryName(new Uri(this.GetType().GetTypeInfo().Assembly.CodeBase).LocalPath);

        protected virtual string UnmanagedDllDirectory => null;

        /// <inheritdoc/>
        public override bool Execute()
        {
#if NETCOREAPP
            string taskAssemblyPath = new Uri(this.GetType().GetTypeInfo().Assembly.CodeBase).LocalPath;

            Assembly inContextAssembly = GitLoaderContext.Instance.LoadFromAssemblyPath(taskAssemblyPath);
            Type innerTaskType = inContextAssembly.GetType(this.GetType().FullName);
            object innerTask = Activator.CreateInstance(innerTaskType);

            var outerProperties = this.GetType().GetRuntimeProperties().ToDictionary(i => i.Name);
            var innerProperties = innerTaskType.GetRuntimeProperties().ToDictionary(i => i.Name);
            var propertiesDiscovery = from outerProperty in outerProperties.Values
                                      where outerProperty.SetMethod is not null && outerProperty.GetMethod is not null
                                      let innerProperty = innerProperties[outerProperty.Name]
                                      select new { outerProperty, innerProperty };
            var propertiesMap = propertiesDiscovery.ToArray();
            var outputPropertiesMap = propertiesMap.Where(pair => pair.outerProperty.GetCustomAttribute<OutputAttribute>() is not null).ToArray();

            foreach (var propertyPair in propertiesMap)
            {
                object outerPropertyValue = propertyPair.outerProperty.GetValue(this);
                propertyPair.innerProperty.SetValue(innerTask, outerPropertyValue);
            }

            MethodInfo executeInnerMethod = innerTaskType.GetMethod(nameof(this.ExecuteInner), BindingFlags.Instance | BindingFlags.NonPublic);
            bool result = (bool)executeInnerMethod.Invoke(innerTask, new object[0]);

            foreach (var propertyPair in outputPropertiesMap)
            {
                propertyPair.outerProperty.SetValue(this, propertyPair.innerProperty.GetValue(innerTask));
            }

            return result;
#else
            // On .NET Framework (on Windows), we find native binaries by adding them to our PATH.
            if (this.UnmanagedDllDirectory is not null)
            {
                string pathEnvVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                string[] searchPaths = pathEnvVar.Split(Path.PathSeparator);
                if (!searchPaths.Contains(this.UnmanagedDllDirectory, StringComparer.OrdinalIgnoreCase))
                {
                    pathEnvVar += Path.PathSeparator + this.UnmanagedDllDirectory;
                    Environment.SetEnvironmentVariable("PATH", pathEnvVar);
                }
            }

            return this.ExecuteInner();
#endif
        }

        protected abstract bool ExecuteInner();
    }
}
