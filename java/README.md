# Nerdbank.GitVersioning for Java

The adapter and Maven extension are distributed through Maven Central.
The Gradle plugin is distributed through the Gradle Plugin Portal.

The Java integrations consume the narrow, versioned
[`nbgv get-version --format adapter`](../docfx/docs/adapter-json.md) contract. They never install
`nbgv` and launch it directly, without a shell. Executable resolution is:

1. adapter-specific explicit configuration;
2. `NBGV_EXECUTABLE`;
3. `nbgv` on `PATH`.

Either the native Rust or .NET CLI supports this contract.
Using the native CLI does not require a .NET runtime.

Each project uses the nearest `version.json` in its directory or an ancestor. Projects sharing that
file share one calculation. Versions default to the immutable full SemVer 2 value, including
prerelease and build metadata; these integrations do not append `-SNAPSHOT`.

## Gradle binary plugin

Apply `com.microsoft.nerdbank-gitversioning`. The plugin uses a Gradle `ValueSource`, sets
`project.version` and Maven/Ivy publication versions, and adds tracked inputs and these attributes to
every `Jar` manifest:

* `Implementation-Version`
* `NBGV-Numeric-Version`
* `NBGV-Version-Height`
* `NBGV-Public-Release`
* `Git-Commit-Id`, `Git-Commit-Id-Short`, and `Git-Commit-Date` when Git data is available

```groovy
plugins {
    id 'java-library'
    id 'maven-publish'
    id 'com.microsoft.nerdbank-gitversioning' version '<version>'
}

// Optional. It must be one executable, not a command with arguments.
nbgv {
    executable = '/tools/nbgv'
}
```

The Groovy assignment above (or `executable('/tools/nbgv')`) immediately assigns the calculated
string to `project.version`; configuration logic after the `nbgv` block does not wait for
`afterEvaluate`. Kotlin code using `executable.set(...)` still sees the lazy calculated value via
`project.version.toString()` until configuration completes. For convention plugins that consume the
version before the block, use `-Pnbgv.executable=/tools/nbgv` or put
`nbgv.executable=/tools/nbgv` in `gradle.properties`; this assigns the string when the plugin is
applied.
Changing `version.json` invalidates the value source. On configuration-cache reuse Gradle reevaluates
the value source and invalidates the entry when `nbgv` reports different Git-derived data.

## Maven core extension

Install the extension through `.mvn/extensions.xml`:

```xml
<extensions>
  <extension>
    <groupId>com.microsoft.nerdbank-gitversioning</groupId>
    <artifactId>nbgv-maven-extension</artifactId>
    <version><!-- integration version --></version>
  </extension>
</extensions>
```

Use `-Dnbgv.executable=/tools/nbgv` for explicit selection. A Maven 3-compatible `ModelProcessor`
stamps raw project models before Maven constructs projects and its reactor GAV index. Project,
relative-parent, dependency, and dependency-management coordinates therefore participate in reactor
sorting and resolution under their final versions. It exposes `nbgv.semVer2`, `nbgv.version`,
`nbgv.versionHeight`, `nbgv.publicRelease`, and available Git values as model properties. External
parent and dependency POMs without a nearby `version.json` are left unchanged.
The extension discovers active modules before returning the first stamped model, using Maven's profile selection and property interpolation.
Consumer-before-library ordering is supported for literal dependency versions, including references in profiles and dependency management.

### Maven 3 CI-friendly versions and flatten

For published POMs on Maven 3, use CI-friendly `revision` plus the flatten plugin. The fallback lets
Maven initially parse the raw POM; the model hook replaces it before reactor indexing:

```xml
<version>${revision}</version>
<properties>
  <revision>0.0.0</revision>
</properties>
```

Configure `org.codehaus.mojo:flatten-maven-plugin` with
`<flattenMode>resolveCiFriendliesOnly</flattenMode>` and run its `flatten` goal in
`process-resources` and `clean` goal in `clean`. The extension replaces each project's `revision`
property with its nbgv SemVer 2 value before flattening. Reactor references are updated even when
modules resolve to different nearest `version.json` files.

### Maven 4

Maven 4 can consume the extension through the same `.mvn/extensions.xml` file. It invokes the
legacy-model compatibility `ModelProcessor` early enough to stamp the build model; it does not
depend on Maven 3 reactor-map internals. CI-friendly `${revision}` remains supported, although Maven
4 users may use its native build/consumer POM features instead of flatten. Keep the fallback
`revision` for builds that must also run on Maven 3.

## Building and testing

From this directory, use Java 17 and a local Gradle 8.8 or newer:

```console
gradle clean test \
  :adapter-core:publishAllPublicationsToStagingRepository \
  :maven-extension:publishAllPublicationsToStagingRepository \
  :gradle-plugin:publishAllPublicationsToStagingRepository
```

The command stages a local Maven repository in `build/maven-repository`.
The group is `com.microsoft.nerdbank-gitversioning`, with artifacts
`nbgv-adapter`, `nbgv-maven-extension`, and `nbgv-gradle-plugin`, all initially
at version 0.1.0.
See the [Java acquisition and migration guide][java-docs] and
[release checklist][release-docs].

[java-docs]: ../docfx/docs/ecosystems/java.md
[release-docs]: ../docfx/docs/ecosystem-releases.md
