# Microsoft's (internal) quickbuild

Nerdbank.GitVersioning supports the Microsoft-internal quickbuild/cloudbuild tool.

It works out of the box, but each project will recompute the version, which may accumulate to a significant increase in overall build time.

🚧 A future version of Nerdbank.GitVersioning will cache version information as a file so that the following instructions will be effective. 🚧

To calculate the version just once for an entire build, a few manual steps are required.

1. Create this project in your repo. The suggested location is `VersionGeneration/VersionGeneration.msbuildproj`.

    ```xml
    <Project Sdk="Microsoft.Build.NoTargets">
      <PropertyGroup>
        <TargetFramework>net5.0</TargetFramework>
        <IsPackable>false</IsPackable>
        <SkipCopyBuildProduct>true</SkipCopyBuildProduct>
        <NBGV_CacheMode>VersionGenerationTarget</NBGV_CacheMode>
      </PropertyGroup>
    </Project>
    ```

    The `TargetFramework` property value is not important as no assemblies are built by this project,
    but a value is nonetheless required for NuGet to be willing to consume the Nerdbank.GitVersioning package reference
    (which is referenced in Directory.Build.props as described later).

1. Add the SDK version to your repo-root level `global.json` file, if it is not already present.
    The [latest available version from nuget.org](https://www.nuget.org/packages/microsoft.build.notargets) is recommended.

    ```json
    {
      "msbuild-sdks": {
        "Microsoft.Build.NoTargets": "3.1.0"
      }
    }
    ```

1. Modify your repo-root level `Directory.Build.props` file to contain these elements:

    ```xml
    <PropertyGroup>
      <!-- This entire repo has just one version.json file, so compute the version   once and share with all projects in a large build. -->
      <GitVersionBaseDirectory>$(MSBuildThisFileDirectory)</GitVersionBaseDirectory>
    </PropertyGroup>

    <PropertyGroup Condition=" '$(QBuild)' == '1' ">
      <NBGV_CacheMode>MSBuildTargetCaching</NBGV_CacheMode>
      <NBGV_CachingProjectReference>$(MSBuildThisFileDirectory)VersionGeneration\VersionGeneration.msbuildproj</NBGV_CachingProjectReference>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Nerdbank.GitVersioning" Version="3.5.*" PrivateAssets="all" />
    </ItemGroup>
    ```
