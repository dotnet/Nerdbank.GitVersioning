package com.microsoft.nbgv.maven;

import com.microsoft.nbgv.adapter.AdapterCalculator;
import com.microsoft.nbgv.adapter.AdapterException;
import com.microsoft.nbgv.adapter.AdapterVersion;
import com.microsoft.nbgv.adapter.VersionFileResolver;
import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.io.Reader;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Properties;
import javax.inject.Named;
import javax.inject.Singleton;
import org.apache.maven.model.Dependency;
import org.apache.maven.model.DependencyManagement;
import org.apache.maven.model.Model;
import org.apache.maven.model.Parent;
import org.apache.maven.model.building.FileModelSource;
import org.apache.maven.model.building.ModelProcessor;
import org.apache.maven.model.io.ModelReader;
import org.apache.maven.model.locator.ModelLocator;

/**
 * Stamps raw Maven models before reactor projects and Maven 3's reactor GAV index are built.
 */
@Named
@Singleton
public final class NbgvModelProcessor implements ModelProcessor {
    private final AdapterCalculator calculator = new AdapterCalculator();
    private final Map<String, String> reactorVersions = new HashMap<String, String>();
    private final List<Model> reactorModels = new ArrayList<Model>();
    private ModelLocator locator;
    private ModelReader reader;

    /**
     * Sets the standard Maven model locator used by this decorating processor.
     *
     * @param locator Maven's default model locator
     */
    public void setModelLocator(ModelLocator locator) {
        this.locator = locator;
    }

    /**
     * Sets the standard Maven model reader used by this decorating processor.
     *
     * @param reader Maven's default model reader
     */
    public void setModelReader(ModelReader reader) {
        this.reader = reader;
    }

    /** Delegates POM location to Maven's standard locator. */
    @Override
    public File locatePom(File projectDirectory) {
        return requireLocator().locatePom(projectDirectory);
    }

    /** Reads and stamps a file-backed model before Maven constructs reactor projects. */
    @Override
    public Model read(File input, Map<String, ?> options) throws IOException {
        Model model = requireReader().read(input, options);
        return stamp(model, input.toPath());
    }

    /** Reads and stamps a model when Maven supplies its file source in the options. */
    @Override
    public Model read(Reader input, Map<String, ?> options) throws IOException {
        Model model = requireReader().read(input, options);
        Path pom = sourcePath(options);
        return pom == null ? model : stamp(model, pom);
    }

    /** Reads and stamps a model when Maven supplies its file source in the options. */
    @Override
    public Model read(InputStream input, Map<String, ?> options) throws IOException {
        Model model = requireReader().read(input, options);
        Path pom = sourcePath(options);
        return pom == null ? model : stamp(model, pom);
    }

    private synchronized Model stamp(Model model, Path pomFile) throws IOException {
        Path projectDirectory = pomFile.toAbsolutePath().normalize().getParent();
        if (projectDirectory == null) {
            throw new IOException("Cannot determine the project directory for " + pomFile + ".");
        }

        try {
            try {
                VersionFileResolver.findNearest(projectDirectory);
            } catch (AdapterException noVersionConfiguration) {
                // Maven also sends dependency and external parent POMs through this global hook.
                return model;
            }

            AdapterVersion adapter =
                    calculator.calculate(projectDirectory, System.getProperty("nbgv.executable"));
            applyVersion(model, adapter);
            stampRelativeParent(model, projectDirectory);
            reactorVersions.put(coordinates(groupId(model), model.getArtifactId()), adapter.getSemVer2());
            reactorModels.add(model);
            for (Model reactorModel : reactorModels) {
                updateReactorReferences(reactorModel);
            }

            return model;
        } catch (AdapterException e) {
            throw new IOException(
                    "Nerdbank.GitVersioning could not stamp Maven model '" + pomFile + "': " + e.getMessage(), e);
        }
    }

    private void stampRelativeParent(Model model, Path projectDirectory) {
        Parent parent = model.getParent();
        if (parent == null) {
            return;
        }

        String relativePath = parent.getRelativePath();
        if (relativePath == null) {
            relativePath = "../pom.xml";
        } else if (relativePath.isEmpty()) {
            return;
        }

        Path parentPath = projectDirectory.resolve(relativePath).normalize();
        if (parentPath.toFile().isDirectory()) {
            parentPath = parentPath.resolve("pom.xml");
        }

        if (parentPath.toFile().isFile()) {
            try {
                VersionFileResolver.findNearest(parentPath.getParent());
            } catch (AdapterException noParentVersionConfiguration) {
                return;
            }

            AdapterVersion parentVersion =
                    calculator.calculate(parentPath.getParent(), System.getProperty("nbgv.executable"));
            parent.setVersion(parentVersion.getSemVer2());
        }
    }

    private void updateReactorReferences(Model model) {
        Parent parent = model.getParent();
        if (parent != null) {
            String version = reactorVersions.get(coordinates(parent.getGroupId(), parent.getArtifactId()));
            if (version != null) {
                parent.setVersion(version);
            }
        }

        updateDependencies(model.getDependencies());
        DependencyManagement management = model.getDependencyManagement();
        if (management != null) {
            updateDependencies(management.getDependencies());
        }
    }

    private void updateDependencies(List<Dependency> dependencies) {
        for (Dependency dependency : dependencies) {
            String version =
                    reactorVersions.get(coordinates(dependency.getGroupId(), dependency.getArtifactId()));
            if (version != null) {
                dependency.setVersion(version);
            }
        }
    }

    private static void applyVersion(Model model, AdapterVersion adapter) {
        String semVer2 = adapter.getSemVer2();
        model.setVersion(semVer2);
        Properties properties = model.getProperties();
        properties.setProperty("revision", semVer2);
        properties.setProperty("nbgv.semVer2", semVer2);
        properties.setProperty("nbgv.version", adapter.getVersion());
        properties.setProperty("nbgv.versionHeight", Integer.toString(adapter.getVersionHeight()));
        properties.setProperty("nbgv.publicRelease", Boolean.toString(adapter.isPublicRelease()));
        setOptionalProperty(properties, "nbgv.gitCommitId", adapter.getGitCommitId().orElse(null));
        setOptionalProperty(properties, "nbgv.gitCommitIdShort", adapter.getGitCommitIdShort().orElse(null));
        setOptionalProperty(
                properties,
                "nbgv.gitCommitDate",
                adapter.getGitCommitDate().isPresent() ? adapter.getGitCommitDate().get().toString() : null);
    }

    private static String groupId(Model model) {
        if (model.getGroupId() != null) {
            return model.getGroupId();
        }

        return model.getParent() == null ? null : model.getParent().getGroupId();
    }

    private static String coordinates(String groupId, String artifactId) {
        return groupId + ":" + artifactId;
    }

    private static void setOptionalProperty(Properties properties, String name, String value) {
        if (value == null) {
            properties.remove(name);
        } else {
            properties.setProperty(name, value);
        }
    }

    private static Path sourcePath(Map<String, ?> options) {
        Object source = options == null ? null : options.get(ModelProcessor.SOURCE);
        if (source instanceof FileModelSource) {
            return ((FileModelSource) source).getPomFile().toPath();
        }

        return null;
    }

    private ModelLocator requireLocator() {
        if (locator == null) {
            throw new IllegalStateException("Maven did not inject its model locator.");
        }

        return locator;
    }

    private ModelReader requireReader() {
        if (reader == null) {
            throw new IllegalStateException("Maven did not inject its model reader.");
        }

        return reader;
    }
}
