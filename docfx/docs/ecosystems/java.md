# Java, Maven, and Gradle

The integrations launch `nbgv` directly and consume the versioned
[adapter JSON contract](../adapter-json.md). Install the native Rust or .NET `nbgv` CLI and
make it available on `PATH`, set `NBGV_EXECUTABLE`, or use the adapter-specific
executable setting.

## Packages

| Integration | Coordinate |
| --- | --- |
| Shared adapter | `com.microsoft.nerdbank-gitversioning:nbgv-adapter:0.1.0` |
| Maven core extension | `com.microsoft.nerdbank-gitversioning:nbgv-maven-extension:0.1.0` |
| Gradle implementation | `com.microsoft.nerdbank-gitversioning:nbgv-gradle-plugin:0.1.0` |
| Gradle plugin ID | `com.microsoft.nerdbank-gitversioning` |

The adapter and Maven extension are available from Maven Central.
The Gradle plugin is available from the Gradle Plugin Portal.
The published bytecode targets Java 8.
See the [Java README][java-readme] for contributor build instructions.

## Gradle plugin

Apply the plugin in your Gradle build:

```groovy
plugins {
    id 'java-library'
    id 'maven-publish'
    id 'com.microsoft.nerdbank-gitversioning' version '0.1.0'
}
```

The plugin sets `project.version`, Maven/Ivy publication versions, and JAR
manifest attributes. Configure an explicit executable with:

```groovy
nbgv {
    executable = '/tools/nbgv'
}
```

## Maven core extension

Add the extension to `.mvn/extensions.xml`:

```xml
<extensions>
  <extension>
    <groupId>com.microsoft.nerdbank-gitversioning</groupId>
    <artifactId>nbgv-maven-extension</artifactId>
    <version>0.1.0</version>
  </extension>
</extensions>
```

The extension stamps project models before Maven indexes the reactor.
Maven 3 publishing should use CI-friendly `${revision}` plus `flatten-maven-plugin`; detailed configuration and Maven 4 notes are in the
[Java README][java-readme].

## Migrate an existing build

1. Remove other plugins or scripts that assign `project.version` from Git.
2. Keep a harmless Maven `${revision}` fallback so the reactor can be read
   before the extension replaces it.
3. Apply the Gradle plugin only once to each versioned project and remove
   custom manifest stamping for attributes it owns.
4. Remove automatic `-SNAPSHOT` suffixing if immutable Nerdbank.GitVersioning
   SemVer is required.
5. Verify artifact versions and manifest metadata in a test publication before releasing your project.

[java-readme]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/java/README.md
