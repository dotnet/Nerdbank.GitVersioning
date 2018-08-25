namespace MSBuildExtensionTask
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
#if NETCOREAPP2_0
    using System.Runtime.Loader;
#endif
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    public abstract class ContextAwareTask : Task
    {
#if NETCOREAPP2_0
        /// <summary>
        /// Our custom <see cref="AssemblyLoadContext"/> that we create to host our Task.
        /// </summary>
        /// <remarks>
        /// We only create *one* of these, and reuse it for subsequent task invocations,
        /// because creating multiple of them hit https://github.com/dotnet/coreclr/issues/19654
        /// </remarks>
        private static CustomAssemblyLoader Loader;
#endif

        protected virtual string ManagedDllDirectory => Path.GetDirectoryName(new Uri(this.GetType().GetTypeInfo().Assembly.CodeBase).LocalPath);

        protected virtual string UnmanagedDllDirectory => null;

        public override bool Execute()
        {
#if NETCOREAPP2_0
            string taskAssemblyPath = new Uri(this.GetType().GetTypeInfo().Assembly.CodeBase).LocalPath;
            if (Loader == null)
            {
                Loader = new CustomAssemblyLoader();
            }

            Loader.LoaderTask = this;
            Assembly inContextAssembly = Loader.LoadFromAssemblyPath(taskAssemblyPath);
            Type innerTaskType = inContextAssembly.GetType(this.GetType().FullName);
            object innerTask = Activator.CreateInstance(innerTaskType);

            var outerProperties = this.GetType().GetRuntimeProperties().ToDictionary(i => i.Name);
            var innerProperties = innerTaskType.GetRuntimeProperties().ToDictionary(i => i.Name);
            var propertiesDiscovery = from outerProperty in outerProperties.Values
                                      where outerProperty.SetMethod != null && outerProperty.GetMethod != null
                                      let innerProperty = innerProperties[outerProperty.Name]
                                      select new { outerProperty, innerProperty };
            var propertiesMap = propertiesDiscovery.ToArray();
            var outputPropertiesMap = propertiesMap.Where(pair => pair.outerProperty.GetCustomAttribute<OutputAttribute>() != null).ToArray();

            foreach (var propertyPair in propertiesMap)
            {
                object outerPropertyValue = propertyPair.outerProperty.GetValue(this);
                propertyPair.innerProperty.SetValue(innerTask, outerPropertyValue);
            }

            var executeInnerMethod = innerTaskType.GetMethod(nameof(ExecuteInner), BindingFlags.Instance | BindingFlags.NonPublic);
            bool result = (bool)executeInnerMethod.Invoke(innerTask, new object[0]);

            foreach (var propertyPair in outputPropertiesMap)
            {
                propertyPair.outerProperty.SetValue(this, propertyPair.innerProperty.GetValue(innerTask));
            }

            return result;
#else
            // On .NET Framework (on Windows), we find native binaries by adding them to our PATH.
            if (this.UnmanagedDllDirectory != null)
            {
                string pathEnvVar = Environment.GetEnvironmentVariable("PATH");
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

#if NETCOREAPP2_0
        private class CustomAssemblyLoader : AssemblyLoadContext
        {
            private ContextAwareTask loaderTask;

            internal CustomAssemblyLoader()
            {
            }

            internal ContextAwareTask LoaderTask
            {
                get => this.loaderTask;
                set => this.loaderTask = value;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                if (this.loaderTask == null)
                {
                    throw new InvalidOperationException(nameof(this.loaderTask) + " must be set first.");
                }

                string assemblyPath = Path.Combine(this.loaderTask.ManagedDllDirectory, assemblyName.Name) + ".dll";
                if (File.Exists(assemblyPath))
                {
                    return this.LoadFromAssemblyPath(assemblyPath);
                }

                return Default.LoadFromAssemblyName(assemblyName);
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                if (this.loaderTask == null)
                {
                    throw new InvalidOperationException(nameof(this.loaderTask) + " must be set first.");
                }

                string unmanagedDllPath = Directory.EnumerateFiles(
                    this.loaderTask.UnmanagedDllDirectory,
                    $"{unmanagedDllName}.*").Concat(
                        Directory.EnumerateFiles(
                            this.loaderTask.UnmanagedDllDirectory,
                            $"lib{unmanagedDllName}.*"))
                    .FirstOrDefault();
                if (unmanagedDllPath != null)
                {
                    return this.LoadUnmanagedDllFromPath(unmanagedDllPath);
                }

                return base.LoadUnmanagedDll(unmanagedDllName);
            }
        }
#endif
    }
}
