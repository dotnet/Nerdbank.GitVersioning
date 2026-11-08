package com.microsoft.nbgv.maven;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.Collections;
import org.apache.maven.model.Model;
import org.apache.maven.model.io.DefaultModelReader;
import org.apache.maven.model.locator.DefaultModelLocator;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class NbgvModelProcessorTest {
    @TempDir
    Path temporaryDirectory;

    @AfterEach
    void clearExecutable() {
        System.clearProperty("nbgv.executable");
    }

    @Test
    void stampsModelsParentsAndDependenciesBeforeProjectConstruction() throws IOException {
        Files.write(temporaryDirectory.resolve("version.json"), Arrays.asList("{}"), StandardCharsets.UTF_8);
        Path fakeNbgv = createFakeNbgv();
        System.setProperty("nbgv.executable", fakeNbgv.toString());

        Path parentPom = writePom(
                temporaryDirectory.resolve("pom.xml"),
                "<groupId>example</groupId><artifactId>parent</artifactId><version>${revision}</version>"
                        + "<packaging>pom</packaging><properties><revision>0.0.0</revision></properties>");
        Path libraryDirectory = Files.createDirectory(temporaryDirectory.resolve("library"));
        Path libraryPom = writePom(
                libraryDirectory.resolve("pom.xml"),
                "<parent><groupId>example</groupId><artifactId>parent</artifactId>"
                        + "<version>${revision}</version></parent><artifactId>library</artifactId>");
        Path appDirectory = Files.createDirectory(temporaryDirectory.resolve("app"));
        Path appPom = writePom(
                appDirectory.resolve("pom.xml"),
                "<parent><groupId>example</groupId><artifactId>parent</artifactId>"
                        + "<version>${revision}</version></parent><artifactId>app</artifactId>"
                        + "<dependencies><dependency><groupId>example</groupId><artifactId>library</artifactId>"
                        + "<version>${revision}</version></dependency></dependencies>");

        NbgvModelProcessor processor = processor();
        Model parent = processor.read(parentPom.toFile(), Collections.<String, Object>emptyMap());
        Model app = processor.read(appPom.toFile(), Collections.<String, Object>emptyMap());
        Model library = processor.read(libraryPom.toFile(), Collections.<String, Object>emptyMap());

        assertEquals("2.3.4-beta.5+build.7", parent.getVersion());
        assertEquals("2.3.4-beta.5+build.7", app.getVersion());
        assertEquals("2.3.4-beta.5+build.7", app.getParent().getVersion());
        assertEquals("2.3.4-beta.5+build.7", app.getDependencies().get(0).getVersion());
        assertEquals("2.3.4-beta.5+build.7", library.getVersion());
        assertEquals("2.3.4-beta.5+build.7", library.getProperties().getProperty("revision"));
    }

    private NbgvModelProcessor processor() {
        NbgvModelProcessor processor = new NbgvModelProcessor();
        processor.setModelLocator(new DefaultModelLocator());
        processor.setModelReader(new DefaultModelReader());
        return processor;
    }

    private Path writePom(Path path, String body) throws IOException {
        Files.write(
                path,
                Arrays.asList(
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
                        "<project xmlns=\"http://maven.apache.org/POM/4.0.0\">",
                        "<modelVersion>4.0.0</modelVersion>",
                        body,
                        "</project>"),
                StandardCharsets.UTF_8);
        return path;
    }

    private Path createFakeNbgv() throws IOException {
        String json = "{\"schemaVersion\":1,\"semVer2\":\"2.3.4-beta.5+build.7\","
                + "\"version\":\"2.3.4.5\",\"versionHeight\":5,"
                + "\"gitCommitId\":\"0123456789abcdef\",\"gitCommitIdShort\":\"\","
                + "\"gitCommitDate\":null,\"publicRelease\":false}";
        if (System.getProperty("os.name").toLowerCase().contains("win")) {
            Path script = temporaryDirectory.resolve("fake-nbgv.cmd");
            Files.write(script, Arrays.asList("@echo off", "echo " + json), StandardCharsets.US_ASCII);
            return script;
        }

        Path script = temporaryDirectory.resolve("fake-nbgv");
        Files.write(
                script,
                Arrays.asList("#!/bin/sh", "printf '%s\\n' '" + json + "'"),
                StandardCharsets.US_ASCII);
        script.toFile().setExecutable(true);
        return script;
    }
}
