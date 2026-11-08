# Java, Maven, and Gradle

> [!IMPORTANT]
> The Java artifacts and Gradle plugin are currently a source preview. The
> coordinates below are intended for a first release and are not a claim that
> Maven Central or the Gradle Plugin Portal already contains them.

The integrations launch `nbgv` directly and consume the versioned
[adapter JSON contract](../adapter-json.md). Install `nbgv` as a .NET tool and
make it available on `PATH`, set `NBGV_EXECUTABLE`, or use the adapter-specific
executable setting.

## Intended coordinates

| Integration | Intended coordinate |
| --- | --- |
| Shared adapter | `com.microsoft.nerdbank-gitversioning:nbgv-adapter:0.1.0` |
| Maven core extension | `com.microsoft.nerdbank-gitversioning:nbgv-maven-extension:0.1.0` |
| Gradle implementation | `com.microsoft.nerdbank-gitversioning:nbgv-gradle-plugin:0.1.0` |
| Gradle plugin ID | `com.microsoft.nerdbank-gitversioning` |

The names are repository-specific rather than generic Java names. Registry
ownership and collision checks must still be repeated immediately before the
first publication.

## Build and acquire from source

Gradle 8.8 or newer and Java 17 are required to build the adapters. The
published bytecode targets Java 8.

```console
cd java
gradle clean test \
  :adapter-core:publishAllPublicationsToStagingRepository \
  :maven-extension:publishAllPublicationsToStagingRepository \
  :gradle-plugin:publishAllPublicationsToStagingRepository
```

This creates a local Maven repository below `java/build/maven-repository`.
Point a test build at that repository while the adapters remain a source
preview. Sources, Javadoc, POM metadata, license notices, plugin markers, and
Gradle module metadata are generated with the binary JARs.

## Gradle plugin

After a Plugin Portal release, the intended plugin declaration is:

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

After a Maven Central release, the intended `.mvn/extensions.xml` entry is:

```xml
<extensions>
  <extension>
    <groupId>com.microsoft.nerdbank-gitversioning</groupId>
    <artifactId>nbgv-maven-extension</artifactId>
    <version>0.1.0</version>
  </extension>
</extensions>
```

The extension updates the full reactor after projects are read. Maven 3
publishing should use CI-friendly `${revision}` plus
`flatten-maven-plugin`; detailed configuration and Maven 4 notes are in the
[Java README][java-readme].

## Migrate an existing build

1. Remove other plugins or scripts that assign `project.version` from Git.
2. Keep a harmless Maven `${revision}` fallback so the reactor can be read
   before the extension replaces it.
3. Apply the Gradle plugin only once to each versioned project and remove
   custom manifest stamping for attributes it owns.
4. Remove automatic `-SNAPSHOT` suffixing if immutable Nerdbank.GitVersioning
   SemVer is required.
5. Publish the shared adapter and Maven artifacts before the Gradle plugin, then
   test dependency resolution from clean caches.

[java-readme]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/java/README.md
