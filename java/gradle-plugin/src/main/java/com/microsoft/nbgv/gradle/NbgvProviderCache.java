package com.microsoft.nbgv.gradle;

import com.microsoft.nbgv.adapter.AdapterVersion;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;
import org.gradle.api.Project;
import org.gradle.api.provider.Provider;

/**
 * Shares ValueSource providers within one Gradle build without retaining values in a daemon.
 */
final class NbgvProviderCache {
    private final Map<String, Provider<AdapterVersion>> providers =
            new HashMap<String, Provider<AdapterVersion>>();

    synchronized Provider<AdapterVersion> get(
            Project project, Path versionJson, String explicitExecutable) {
        String key = versionJson.toAbsolutePath().normalize().toString()
                + '\0'
                + (explicitExecutable == null ? "" : explicitExecutable);
        Provider<AdapterVersion> existing = providers.get(key);
        if (existing != null) {
            return existing;
        }

        Provider<AdapterVersion> created = project.getProviders().of(
                NbgvValueSource.class,
                specification -> {
                    specification.getParameters().getProjectDirectory().set(versionJson.getParent().toFile());
                    specification.getParameters().getVersionJson().set(versionJson.toFile());
                    if (explicitExecutable != null) {
                        specification.getParameters().getExecutable().set(explicitExecutable);
                    }
                });
        providers.put(key, created);
        return created;
    }
}
