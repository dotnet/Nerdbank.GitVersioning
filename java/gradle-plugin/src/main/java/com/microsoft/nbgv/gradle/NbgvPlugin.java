package com.microsoft.nbgv.gradle;

import com.microsoft.nbgv.adapter.AdapterVersion;
import com.microsoft.nbgv.adapter.VersionFileResolver;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.Map;
import org.gradle.api.Plugin;
import org.gradle.api.Project;
import org.gradle.api.provider.Provider;
import org.gradle.api.publish.PublishingExtension;
import org.gradle.api.publish.ivy.IvyPublication;
import org.gradle.api.publish.maven.MavenPublication;
import org.gradle.api.tasks.bundling.Jar;

/**
 * Applies immutable Nerdbank.GitVersioning versions to a Gradle project.
 */
public final class NbgvPlugin implements Plugin<Project> {
    /** Applies the plugin. */
    @Override
    public void apply(Project project) {
        Path versionJson = VersionFileResolver.findNearest(project.getProjectDir().toPath());
        NbgvProviderCache cache = providerCache(project);
        NbgvExtension extension = new NbgvExtension(
                project.getObjects().property(String.class),
                executable -> project.setVersion(
                        cache.get(project, versionJson, executable).get().getSemVer2()));
        project.getExtensions().add("nbgv", extension);
        extension.getExecutable().convention(project.getProviders().gradleProperty("nbgv.executable"));
        LazyNbgvVersion lazyVersion = new LazyNbgvVersion(project, extension, cache, versionJson);
        String conventionExecutable = extension.getExecutable().getOrNull();
        if (conventionExecutable == null) {
            project.setVersion(lazyVersion);
        } else {
            project.setVersion(
                    cache.get(project, versionJson, conventionExecutable).get().getSemVer2());
        }

        project.afterEvaluate(ignored -> {
            applyVersion(project, lazyVersion.adapter().get());
        });
    }

    private static NbgvProviderCache providerCache(Project project) {
        Project root = project.getRootProject();
        String extensionName = "nerdbankGitVersioningProviderCache";
        synchronized (root) {
            Object existing = root.getExtensions().findByName(extensionName);
            if (existing != null) {
                return (NbgvProviderCache) existing;
            }

            NbgvProviderCache cache = new NbgvProviderCache();
            root.getExtensions().add(extensionName, cache);
            return cache;
        }
    }

    private static void applyVersion(Project project, AdapterVersion adapter) {
        String version = adapter.getSemVer2();
        project.setVersion(version);

        project.getPluginManager().withPlugin("maven-publish", ignored -> {
            PublishingExtension publishing = project.getExtensions().getByType(PublishingExtension.class);
            publishing.getPublications().withType(MavenPublication.class).configureEach(
                    publication -> publication.setVersion(version));
        });
        project.getPluginManager().withPlugin("ivy-publish", ignored -> {
            PublishingExtension publishing = project.getExtensions().getByType(PublishingExtension.class);
            publishing.getPublications().withType(IvyPublication.class).configureEach(
                    publication -> publication.setRevision(version));
        });

        project.getTasks().withType(Jar.class).configureEach(jar -> {
            Map<String, Object> attributes = manifestAttributes(adapter);
            attributes.forEach((key, value) -> jar.getInputs().property("nbgv." + key, value));
            jar.getManifest().attributes(attributes);
        });
    }

    private static Map<String, Object> manifestAttributes(AdapterVersion adapter) {
        Map<String, Object> attributes = new LinkedHashMap<String, Object>();
        attributes.put("Implementation-Version", adapter.getSemVer2());
        attributes.put("NBGV-Numeric-Version", adapter.getVersion());
        attributes.put("NBGV-Version-Height", Integer.toString(adapter.getVersionHeight()));
        attributes.put("NBGV-Public-Release", Boolean.toString(adapter.isPublicRelease()));
        adapter.getGitCommitId().ifPresent(value -> attributes.put("Git-Commit-Id", value));
        adapter.getGitCommitIdShort().ifPresent(value -> attributes.put("Git-Commit-Id-Short", value));
        adapter.getGitCommitDate().ifPresent(value -> attributes.put("Git-Commit-Date", value.toString()));
        return attributes;
    }
}
