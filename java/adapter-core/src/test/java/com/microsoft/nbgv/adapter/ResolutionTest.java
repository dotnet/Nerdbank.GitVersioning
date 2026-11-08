package com.microsoft.nbgv.adapter;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class ResolutionTest {
    @TempDir
    Path temporaryDirectory;

    @Test
    void executablePrecedenceIsExplicitEnvironmentThenPath() {
        Map<String, String> environment = new HashMap<String, String>();
        environment.put("NBGV_EXECUTABLE", "from-environment");
        assertEquals("explicit", NbgvExecutableResolver.resolve("explicit", environment));
        assertEquals("from-environment", NbgvExecutableResolver.resolve(null, environment));
        assertEquals("nbgv", NbgvExecutableResolver.resolve(null, Collections.<String, String>emptyMap()));
        assertThrows(AdapterException.class, () -> NbgvExecutableResolver.resolve("", environment));
    }

    @Test
    void nearestVersionFileWins() throws IOException {
        Path rootVersion = Files.createFile(temporaryDirectory.resolve("version.json"));
        Path child = Files.createDirectories(temporaryDirectory.resolve("a").resolve("b"));
        assertEquals(rootVersion, VersionFileResolver.findNearest(child));

        Path nearer = Files.createFile(temporaryDirectory.resolve("a").resolve("version.json"));
        assertEquals(nearer, VersionFileResolver.findNearest(child));
    }

    @Test
    void nestedRepositoriesAndWorktreesDoNotInheritOuterVersion() throws IOException {
        Files.createFile(temporaryDirectory.resolve("version.json"));
        for (String markerType : new String[] {"directory", "file"}) {
            Path root = Files.createDirectory(temporaryDirectory.resolve(markerType));
            Path marker = root.resolve(".git");
            if (markerType.equals("directory")) {
                Files.createDirectory(marker);
            } else {
                Files.write(marker, Collections.singletonList("gitdir: ../other"));
            }
            Path child = Files.createDirectory(root.resolve("child"));
            assertThrows(AdapterException.class, () -> VersionFileResolver.findNearest(child));
            Path local = Files.createFile(root.resolve("version.json"));
            assertEquals(local, VersionFileResolver.findNearest(child));
        }
    }
}
