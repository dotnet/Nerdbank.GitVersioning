package com.microsoft.nbgv.adapter;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.Map;

/**
 * Directly launches nbgv and parses its adapter output.
 */
public final class NbgvLauncher {
    private final Map<String, String> environment;
    private final AdapterJsonParser parser;

    /**
     * Initializes a launcher that inherits the current process environment.
     */
    public NbgvLauncher() {
        this(System.getenv(), new AdapterJsonParser());
    }

    /**
     * Initializes a launcher.
     *
     * @param environment environment used for executable resolution and the child process
     */
    public NbgvLauncher(Map<String, String> environment) {
        this(environment, new AdapterJsonParser());
    }

    NbgvLauncher(Map<String, String> environment, AdapterJsonParser parser) {
        this.environment = environment;
        this.parser = parser;
    }

    /**
     * Calculates a version from a directory containing version.json.
     *
     * @param workingDirectory directory to run nbgv in
     * @param explicitExecutable explicitly configured executable, or {@code null}
     * @return the validated adapter result
     */
    public AdapterVersion calculate(Path workingDirectory, String explicitExecutable) {
        String executable = NbgvExecutableResolver.resolve(explicitExecutable, environment);
        ProcessBuilder processBuilder = new ProcessBuilder(
                Arrays.asList(executable, "get-version", "--format", "adapter"));
        processBuilder.directory(workingDirectory.toAbsolutePath().normalize().toFile());
        processBuilder.environment().clear();
        processBuilder.environment().putAll(environment);

        final Process process;
        try {
            process = processBuilder.start();
        } catch (IOException e) {
            throw new AdapterException(
                    "Failed to launch nbgv executable '" + executable + "': " + e.getMessage(), e);
        }

        StreamCollector stdout = new StreamCollector(process.getInputStream(), "nbgv-stdout");
        StreamCollector stderr = new StreamCollector(process.getErrorStream(), "nbgv-stderr");
        stdout.start();
        stderr.start();

        final int exitCode;
        try {
            exitCode = process.waitFor();
            stdout.join();
            stderr.join();
        } catch (InterruptedException e) {
            process.destroyForcibly();
            Thread.currentThread().interrupt();
            throw new AdapterException("Interrupted while waiting for nbgv.", e);
        }

        String errorText = stderr.content().trim();
        if (exitCode != 0) {
            String suffix = errorText.isEmpty() ? "" : ": " + errorText;
            throw new AdapterException("nbgv exited with code " + exitCode + suffix);
        }

        if (stdout.failure != null || stderr.failure != null) {
            IOException failure = stdout.failure != null ? stdout.failure : stderr.failure;
            throw new AdapterException("Failed to read nbgv process output.", failure);
        }

        return parser.parse(stdout.content());
    }

    private static final class StreamCollector extends Thread {
        private final InputStream stream;
        private final ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        private IOException failure;

        StreamCollector(InputStream stream, String name) {
            super(name);
            this.stream = stream;
            setDaemon(true);
        }

        @Override
        public void run() {
            byte[] buffer = new byte[4096];
            try (InputStream input = stream) {
                int count;
                while ((count = input.read(buffer)) >= 0) {
                    bytes.write(buffer, 0, count);
                }
            } catch (IOException e) {
                failure = e;
            }
        }

        String content() {
            return new String(bytes.toByteArray(), StandardCharsets.UTF_8);
        }
    }
}
