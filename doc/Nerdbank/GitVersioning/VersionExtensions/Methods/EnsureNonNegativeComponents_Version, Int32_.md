# VersionExtensions.EnsureNonNegativeComponents(Version, Int32) Method
> Returns a  instance where the specified number of components
            are guaranteed to be non-negative. Any applicable negative components are converted to zeros.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static System.Version EnsureNonNegativeComponents(this System.Version version, int fieldCount = 4);
~~~~
##### Parameters
*version*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Version

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The version to use as a template for the returned value.


*fieldCount*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Int32

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The number of version components to ensure are non-negative.


##### Return Value
Type: Version


            The same as  except with any applicable negative values
            translated to zeros.
            

