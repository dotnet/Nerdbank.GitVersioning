# VersionExtensions.ToStringSafe(Version, Int32) Method
> Converts the value of the current System.Version object to its equivalent System.String
            representation. A specified count indicates the number of components to return.

**Namespace:** Nerdbank.GitVersioning

**Assembly:** NerdBank.GitVersioning (in NerdBank.GitVersioning.dll)
## Syntax
~~~~csharp
public static string ToStringSafe(this System.Version version, int fieldCount);
~~~~
##### Parameters
*version*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Version

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The instance to serialize as a string.


*fieldCount*

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Type: Int32

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;The number of components to return. The fieldCount ranges from 0 to 4.


##### Return Value
Type: String


            The System.String representation of the values of the major, minor, build, and
            revision components of the current System.Version object, each separated by a
            period character ('.'). The fieldCount parameter determines how many components
            are returned.fieldCount Return Value 0 An empty string (""). 1 major 2 major.minor
            3 major.minor.build 4 major.minor.build.revision For example, if you create System.Version
            object using the constructor Version(1,3,5), ToString(2) returns "1.3" and ToString(4)
            returns "1.3.5.0".
            

