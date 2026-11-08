package com.microsoft.nbgv.adapter;

import java.nio.file.Path;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;

/**
 * Resolves project version files and shares one nbgv calculation per version root.
 */
public final class AdapterCalculator {
    private final Map<String, String> environment;
    private final NbgvLauncher launcher;
    private final ConcurrentMap<CacheKey, AdapterVersion> versions = new ConcurrentHashMap<CacheKey, AdapterVersion>();

    /**
     * Initializes a calculator using the current process environment.
     */
    public AdapterCalculator() {
        this(System.getenv());
    }

    /**
     * Initializes a calculator with an environment.
     *
     * @param environment environment used for resolution and child processes
     */
    public AdapterCalculator(Map<String, String> environment) {
        this.environment = environment;
        this.launcher = new NbgvLauncher(environment);
    }

    /**
     * Calculates the version associated with the nearest version.json.
     *
     * @param projectDirectory project to version
     * @param explicitExecutable explicitly configured executable, or {@code null}
     * @return a cached validated adapter value
     */
    public AdapterVersion calculate(Path projectDirectory, String explicitExecutable) {
        Path versionFile = VersionFileResolver.findNearest(projectDirectory);
        Path versionRoot = versionFile.getParent();
        String executable = NbgvExecutableResolver.resolve(explicitExecutable, environment);
        CacheKey key = new CacheKey(versionRoot, executable);
        return versions.computeIfAbsent(key, unused -> launcher.calculate(versionRoot, explicitExecutable));
    }

    private static final class CacheKey {
        private final Path root;
        private final String executable;

        CacheKey(Path root, String executable) {
            this.root = root.toAbsolutePath().normalize();
            this.executable = executable;
        }

        @Override
        public boolean equals(Object obj) {
            if (this == obj) {
                return true;
            }

            if (!(obj instanceof CacheKey)) {
                return false;
            }

            CacheKey other = (CacheKey) obj;
            return root.equals(other.root) && executable.equals(other.executable);
        }

        @Override
        public int hashCode() {
            return 31 * root.hashCode() + executable.hashCode();
        }
    }
}
