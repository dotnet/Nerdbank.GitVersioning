package com.microsoft.nbgv.adapter;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.HashMap;
import java.util.Map;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;

class SharedParityTest {
    @Test
    void sharedGitFixtureMatchesDotNetOracleByteForByte() throws IOException {
        String manifestPath = System.getenv("NBGV_PARITY_MANIFEST");
        Assumptions.assumeTrue(
                manifestPath != null && !manifestPath.isEmpty(),
                "NBGV_PARITY_MANIFEST is not set; shared parity fixture was not requested");

        ObjectMapper objectMapper = new ObjectMapper();
        ParityManifest manifest = objectMapper.readValue(Paths.get(manifestPath).toFile(), ParityManifest.class);
        for (ParityScenario scenario : manifest.scenarios) {
            assertTrue(hasGitMarker(Paths.get(scenario.directory)), scenario.name);
            String oracleJson = new String(
                    Files.readAllBytes(Paths.get(scenario.oracle)), StandardCharsets.UTF_8);
            AdapterVersion expected = new AdapterJsonParser().parse(oracleJson);

            Map<String, String> environment = new HashMap<String, String>(System.getenv());
            environment.put("NBGV_GitEngine", scenario.gitEngine);
            AdapterVersion actual =
                    new NbgvLauncher(environment).calculate(Paths.get(scenario.directory), manifest.executable);

            assertEquals(expected, actual, scenario.name);
            assertArrayEquals(canonicalBytes(expected), canonicalBytes(actual), scenario.name);
        }
    }

    private static boolean hasGitMarker(Path directory) {
        for (Path candidate = directory; candidate != null; candidate = candidate.getParent()) {
            if (Files.exists(candidate.resolve(".git"))) {
                return true;
            }
        }

        return false;
    }

    private static byte[] canonicalBytes(AdapterVersion version) {
        String canonical = version.getSemVer2()
                + '\0'
                + version.getVersion()
                + '\0'
                + version.getVersionHeight()
                + '\0'
                + version.getGitCommitId().orElse("<null>")
                + '\0'
                + version.getGitCommitIdShort().orElse("<null>")
                + '\0'
                + (version.getGitCommitDate().isPresent()
                        ? version.getGitCommitDate().get().toString()
                        : "<null>")
                + '\0'
                + version.isPublicRelease();
        return canonical.getBytes(StandardCharsets.UTF_8);
    }

    public static final class ParityManifest {
        public String executable;
        public String dotnetExecutable;
        public ParityScenario[] scenarios;
    }

    public static final class ParityScenario {
        public String name;
        public String directory;
        public String oracle;
        public String gitEngine;
    }
}
