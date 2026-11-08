package com.microsoft.nbgv.gradle;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.microsoft.nbgv.adapter.AdapterJsonParser;
import com.microsoft.nbgv.adapter.AdapterVersion;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Arrays;
import java.util.jar.Attributes;
import java.util.jar.JarFile;
import org.gradle.testkit.runner.BuildResult;
import org.gradle.testkit.runner.GradleRunner;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class NbgvPluginFunctionalTest {
    @TempDir
    Path projectDirectory;

    @Test
    void setsProjectPublicationAndTrackedManifestVersions() throws IOException {
        Files.createDirectory(projectDirectory.resolve("child"));
        Files.write(
                projectDirectory.resolve("settings.gradle"),
                Arrays.asList("rootProject.name = 'sample'", "include 'child'"));
        Files.write(projectDirectory.resolve("version.json"), Arrays.asList("{}"));
        Path executable = createFakeNbgv();
        String configuredExecutable = executable.toAbsolutePath().toString()
                .replace("\\", "\\\\")
                .replace("'", "\\'");
        Files.write(
                projectDirectory.resolve("build.gradle"),
                Arrays.asList(
                        "plugins {",
                        "  id 'java'",
                        "  id 'maven-publish'",
                        "  id 'com.microsoft.nerdbank-gitversioning'",
                        "}",
                        "nbgv { executable = '" + configuredExecutable + "' }",
                        "if (!(project.version instanceof String) || project.version != '2.3.4-beta.5+build.7') {",
                        "  throw new GradleException(\"version unavailable during configuration: ${project.version}\")",
                        "}",
                        "publishing { publications { library(MavenPublication) { from components.java } } }",
                        "subprojects {",
                        "  apply plugin: 'java'",
                        "  apply plugin: 'com.microsoft.nerdbank-gitversioning'",
                        "  nbgv { executable = '" + configuredExecutable + "' }",
                        "  if (!(version instanceof String) || version != '2.3.4-beta.5+build.7') {",
                        "    throw new GradleException(\"child version unavailable during configuration: ${version}\")",
                        "  }",
                        "}"),
                StandardCharsets.UTF_8);

        BuildResult first = runner()
                .withArguments(
                        "jar",
                        ":child:jar",
                        "generatePomFileForLibraryPublication",
                        "--configuration-cache",
                        "--stacktrace")
                .build();
        assertTrue("1".equals(readInvocationCount()));
        Path jarPath = projectDirectory.resolve("build/libs/sample-2.3.4-beta.5+build.7.jar");
        assertTrue(Files.isRegularFile(jarPath));
        try (JarFile jar = new JarFile(jarPath.toFile())) {
            Attributes attributes = jar.getManifest().getMainAttributes();
            assertTrue("2.3.4-beta.5+build.7".equals(attributes.getValue("Implementation-Version")));
            assertTrue("0123456789abcdef".equals(attributes.getValue("Git-Commit-Id")));
        }

        String pom = new String(
                Files.readAllBytes(projectDirectory.resolve("build/publications/library/pom-default.xml")),
                StandardCharsets.UTF_8);
        assertTrue(pom.contains("<version>2.3.4-beta.5+build.7</version>"));

        BuildResult second = runner()
                .withArguments(
                        "jar",
                        ":child:jar",
                        "generatePomFileForLibraryPublication",
                        "--configuration-cache",
                        "--stacktrace")
                .build();
        assertTrue(second.getOutput().contains("Configuration cache entry reused."));
        assertTrue("2".equals(readInvocationCount()));
    }

    @Test
    void realNbgvProducesJarMetadataMatchingSharedDotNetOracle() throws IOException {
        String manifestPath = System.getenv("NBGV_PARITY_MANIFEST");
        Assumptions.assumeTrue(
                manifestPath != null && !manifestPath.isEmpty(),
                "NBGV_PARITY_MANIFEST is not set; shared parity fixture was not requested");

        ParityManifest fixture =
                new ObjectMapper().readValue(Paths.get(manifestPath).toFile(), ParityManifest.class);
        ParityScenario scenario = fixture.scenarios[0];
        Path directory = Paths.get(scenario.directory);
        AdapterVersion expected = new AdapterJsonParser().parse(new String(
                Files.readAllBytes(Paths.get(scenario.oracle)), StandardCharsets.UTF_8));
        String executable = fixture.executable.replace("\\", "\\\\").replace("'", "\\'");

        Files.write(
                directory.resolve("settings.gradle"),
                Arrays.asList("rootProject.name = 'adapter-parity-sample'"),
                StandardCharsets.UTF_8);
        Files.write(
                directory.resolve("build.gradle"),
                Arrays.asList(
                        "plugins {",
                        "  id 'java'",
                        "  id 'com.microsoft.nerdbank-gitversioning'",
                        "}",
                        "nbgv { executable = '" + executable + "' }"),
                StandardCharsets.UTF_8);
        Path source = directory.resolve("src/main/java/Parity.java");
        Files.createDirectories(source.getParent());
        Files.write(
                source,
                Arrays.asList("public final class Parity { private Parity() { } }"),
                StandardCharsets.UTF_8);

        GradleRunner.create()
                .withProjectDir(directory.toFile())
                .withPluginClasspath()
                .withArguments("jar", "--stacktrace")
                .build();

        Path jarPath = directory.resolve(
                "build/libs/adapter-parity-sample-" + expected.getSemVer2() + ".jar");
        try (JarFile jar = new JarFile(jarPath.toFile())) {
            Attributes attributes = jar.getManifest().getMainAttributes();
            assertEquals(expected.getSemVer2(), attributes.getValue("Implementation-Version"));
            assertEquals(expected.getVersion(), attributes.getValue("NBGV-Numeric-Version"));
            assertEquals(
                    Integer.toString(expected.getVersionHeight()),
                    attributes.getValue("NBGV-Version-Height"));
            assertEquals(
                    Boolean.toString(expected.isPublicRelease()),
                    attributes.getValue("NBGV-Public-Release"));
            assertEquals(expected.getGitCommitId().orElse(null), attributes.getValue("Git-Commit-Id"));
            assertEquals(
                    expected.getGitCommitIdShort().orElse(null),
                    attributes.getValue("Git-Commit-Id-Short"));
            assertEquals(
                    expected.getGitCommitDate().isPresent()
                            ? expected.getGitCommitDate().get().toString()
                            : null,
                    attributes.getValue("Git-Commit-Date"));
        }
    }

    private GradleRunner runner() {
        return GradleRunner.create()
                .withProjectDir(projectDirectory.toFile())
                .withPluginClasspath()
                .forwardOutput();
    }

    private Path createFakeNbgv() throws IOException {
        String json = "{\"schemaVersion\":1,\"semVer2\":\"2.3.4-beta.5+build.7\","
                + "\"version\":\"2.3.4.5\",\"versionHeight\":5,"
                + "\"gitCommitId\":\"0123456789abcdef\",\"gitCommitIdShort\":\"01234567\","
                + "\"gitCommitDate\":\"2026-08-09T21:59:55Z\",\"publicRelease\":false}";
        if (System.getProperty("os.name").toLowerCase().contains("win")) {
            Path script = projectDirectory.resolve("fake-nbgv.cmd");
            Files.write(
                    script,
                    Arrays.asList(
                            "@echo off",
                            "set count=0",
                            "if exist invocations.txt set /p count=<invocations.txt",
                            "set /a count+=1",
                            ">invocations.txt echo %count%",
                            "echo " + json),
                    StandardCharsets.US_ASCII);
            return script;
        }

        Path script = projectDirectory.resolve("fake-nbgv");
        Files.write(
                script,
                Arrays.asList(
                        "#!/bin/sh",
                        "count=0",
                        "test ! -f invocations.txt || count=$(cat invocations.txt)",
                        "count=$((count + 1))",
                        "printf '%s\\n' \"$count\" > invocations.txt",
                        "printf '%s\\n' '" + json + "'"),
                StandardCharsets.US_ASCII);
        assertTrue(script.toFile().setExecutable(true));
        return script;
    }

    private String readInvocationCount() throws IOException {
        return new String(
                        Files.readAllBytes(projectDirectory.resolve("invocations.txt")),
                        StandardCharsets.US_ASCII)
                .trim();
    }

    public static final class ParityManifest {
        public String executable;
        public ParityScenario[] scenarios;
    }

    public static final class ParityScenario {
        public String name;
        public String directory;
        public String oracle;
        public String gitEngine;
    }
}
