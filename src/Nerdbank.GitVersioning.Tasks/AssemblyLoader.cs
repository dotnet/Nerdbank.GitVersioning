#if NETFRAMEWORK

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ModuleInitializerAttribute : Attribute { }
}

namespace Nerdbank.GitVersioning.Tasks
{
    internal static class AssemblyLoader
    {
        [ModuleInitializer]
        internal static void LoaderInitializer()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var required = new AssemblyName(args.Name);
                string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), required.Name + ".dll");
                if (File.Exists(path))
                {
                    AssemblyName actual = AssemblyName.GetAssemblyName(path);
                    if (actual.Version >= required.Version)
                    {
                        return Assembly.LoadFile(path);
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}

#endif
