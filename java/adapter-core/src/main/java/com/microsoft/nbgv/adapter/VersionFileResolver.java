package com.microsoft.nbgv.adapter;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Objects;

/**
 * Locates the nearest version.json at or above a project directory.
 */
public final class VersionFileResolver {
    private VersionFileResolver() {
    }

    /**
     * Finds the nearest version file.
     *
     * @param projectDirectory the project directory
     * @return the normalized absolute version.json path
     */
    public static Path findNearest(Path projectDirectory) {
        Objects.requireNonNull(projectDirectory, "projectDirectory");
        Path current = projectDirectory.toAbsolutePath().normalize();
        while (current != null) {
            Path candidate = current.resolve("version.json");
            if (Files.isRegularFile(candidate)) {
                return candidate;
            }

            if (Files.exists(current.resolve(".git"))) {
                break;
            }
            current = current.getParent();
        }

        throw new AdapterException(
                "No version.json was found in or above project directory '" + projectDirectory + "'.");
    }
}
