# .NET support

Nerdbank.GitVersioning offers first class version stamping support for .NET assemblies.

## Assembly version generation

During the build it adds source code such as this to your compilation:

```csharp
[assembly: System.Reflection.AssemblyVersion("1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.24.15136")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.24.15136-alpha+g9a7eb6c819")]
```

* The first and second integer components of the versions above come from the 
version file.
* The third integer component of the version here is the height of your git history up to
that point, such that it reliably increases with each release.
* The fourth component (when present) is the first two bytes of the git commit ID, encoded as an integer. This number will appear essentially random, and is not useful in sorting versions. It is useful when you have two branches in git history that have exactly the same major.minor.height version information in order to distinguish which commit it is.
* The -alpha tag also comes from the version file and indicates this is an
unstable version.
* The -g9a7eb6c819 tag is the concatenation of -g and the git commit ID that was built.

This class is also injected into your project at build time:

```csharp
internal sealed partial class ThisAssembly {
    internal const string AssemblyVersion = "1.0";
    internal const string AssemblyFileVersion = "1.0.24.15136";
    internal const string AssemblyInformationalVersion = "1.0.24.15136-alpha+g9a7eb6c819";
}
```

This allows you to actually write source code that can refer to the exact build
number your assembly will be assigned.
