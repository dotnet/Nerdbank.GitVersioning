package com.microsoft.nbgv.gradle;

import com.microsoft.nbgv.adapter.AdapterVersion;
import java.io.Serializable;
import java.nio.file.Path;
import org.gradle.api.Project;
import org.gradle.api.provider.Provider;

/**
 * Exposes the calculated version during project configuration while allowing the nbgv extension
 * block to select an executable first.
 */
final class LazyNbgvVersion implements Serializable {
    private static final long serialVersionUID = 1L;

    private final transient Project project;
    private final transient NbgvExtension extension;
    private final transient NbgvProviderCache cache;
    private final Path versionJson;

    LazyNbgvVersion(
            Project project, NbgvExtension extension, NbgvProviderCache cache, Path versionJson) {
        this.project = project;
        this.extension = extension;
        this.cache = cache;
        this.versionJson = versionJson;
    }

    Provider<AdapterVersion> adapter() {
        return cache.get(project, versionJson, extension.getExecutable().getOrNull());
    }

    @Override
    public String toString() {
        return adapter().get().getSemVer2();
    }
}
