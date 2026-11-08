package com.microsoft.nbgv.maven;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import org.apache.maven.cli.MavenCli;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class MavenReactorFunctionalTest {
    @TempDir
    Path projectDirectory;

    @AfterEach
    void clearExecutable() {
        System.clearProperty("nbgv.executable");
        System.clearProperty("maven.multiModuleProjectDirectory");
    }

    @Test
    void mavenThreeIndexesStampedReactorCoordinatesBeforeSorting() throws IOException {
        Files.write(projectDirectory.resolve("version.json"), Arrays.asList("{}"), StandardCharsets.UTF_8);
        System.setProperty("nbgv.executable", createFakeNbgv().toString());
        System.setProperty("maven.multiModuleProjectDirectory", projectDirectory.toString());
        Files.createDirectories(projectDirectory.resolve("app"));
        Files.createDirectories(projectDirectory.resolve("library"));
        writePom(
                projectDirectory.resolve("pom.xml"),
                "<groupId>example</groupId><artifactId>reactor</artifactId><version>${revision}</version>"
                        + "<packaging>pom</packaging><properties><revision>0.0.0</revision></properties>"
                        + "<modules><module>app</module><module>library</module></modules>");
        writePom(
                projectDirectory.resolve("library/pom.xml"),
                parent() + "<artifactId>library</artifactId>");
        writePom(
                projectDirectory.resolve("app/pom.xml"),
                parent() + "<artifactId>app</artifactId><dependencies><dependency>"
                        + "<groupId>example</groupId><artifactId>library</artifactId>"
                        + "<version>${revision}</version></dependency></dependencies>");

        ByteArrayOutputStream stdout = new ByteArrayOutputStream();
        ByteArrayOutputStream stderr = new ByteArrayOutputStream();
        int result;
        try (PrintStream out = new PrintStream(stdout, true, "UTF-8");
                PrintStream err = new PrintStream(stderr, true, "UTF-8")) {
            result = new MavenCli().doMain(
                    new String[] {"validate", "--batch-mode", "--errors"},
                    projectDirectory.toString(),
                    out,
                    err);
        }

        String output = stdout.toString("UTF-8") + stderr.toString("UTF-8");
        assertEquals(0, result, output);
        assertTrue(output.contains("2.3.4-beta.5+build.7"), output);
        int buildOrder = output.indexOf("Reactor Build Order:");
        int library = output.indexOf("library", buildOrder);
        int app = output.indexOf("app", buildOrder);
        assertTrue(buildOrder >= 0 && library > buildOrder && app > library, output);
    }

    private String parent() {
        return "<parent><groupId>example</groupId><artifactId>reactor</artifactId>"
                + "<version>${revision}</version></parent>";
    }

    private void writePom(Path path, String body) throws IOException {
        Files.write(
                path,
                Arrays.asList(
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
                        "<project xmlns=\"http://maven.apache.org/POM/4.0.0\">",
                        "<modelVersion>4.0.0</modelVersion>",
                        body,
                        "</project>"),
                StandardCharsets.UTF_8);
    }

    private Path createFakeNbgv() throws IOException {
        String json = "{\"schemaVersion\":1,\"semVer2\":\"2.3.4-beta.5+build.7\","
                + "\"version\":\"2.3.4.5\",\"versionHeight\":5,"
                + "\"gitCommitId\":null,\"gitCommitIdShort\":\"\","
                + "\"gitCommitDate\":null,\"publicRelease\":false}";
        if (System.getProperty("os.name").toLowerCase().contains("win")) {
            Path script = projectDirectory.resolve("fake-nbgv.cmd");
            Files.write(script, Arrays.asList("@echo off", "echo " + json), StandardCharsets.US_ASCII);
            return script;
        }

        Path script = projectDirectory.resolve("fake-nbgv");
        Files.write(
                script,
                Arrays.asList("#!/bin/sh", "printf '%s\\n' '" + json + "'"),
                StandardCharsets.US_ASCII);
        script.toFile().setExecutable(true);
        return script;
    }
}
