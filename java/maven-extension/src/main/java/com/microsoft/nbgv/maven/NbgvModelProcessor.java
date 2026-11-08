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
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Properties;
import java.util.Set;
import javax.inject.Inject;
import javax.inject.Named;
import javax.inject.Provider;
import javax.inject.Singleton;
import org.apache.maven.execution.MavenSession;
import org.apache.maven.model.Dependency;
import org.apache.maven.model.DependencyManagement;
import org.apache.maven.model.Model;
import org.apache.maven.model.Parent;
import org.apache.maven.model.Profile;
import org.apache.maven.model.building.DefaultModelBuildingRequest;
import org.apache.maven.model.building.FileModelSource;
import org.apache.maven.model.building.ModelProblem;
import org.apache.maven.model.building.ModelProblemCollector;
import org.apache.maven.model.building.ModelProcessor;
import org.apache.maven.model.interpolation.ModelInterpolator;
import org.apache.maven.model.io.ModelReader;
import org.apache.maven.model.locator.ModelLocator;
import org.apache.maven.model.profile.DefaultProfileActivationContext;
import org.apache.maven.model.profile.ProfileSelector;
import org.slf4j.LoggerFactory;

/**
 * Stamps raw Maven models before reactor projects and Maven 3's reactor GAV index are built.
 */
@Named
@Singleton
public final class NbgvModelProcessor implements ModelProcessor {
    private final AdapterCalculator calculator = new AdapterCalculator();
    private final Map<String, String> reactorVersions = new HashMap<String, String>();
    private boolean reactorDiscovered;
    private ModelLocator locator;
    private ModelReader reader;
    @Inject
    private Provider<MavenSession> sessionProvider;
    @Inject
    private ProfileSelector profileSelector;
    @Inject
    private ModelInterpolator interpolator;

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
            MavenSession session = sessionProvider == null ? null : sessionProvider.get();
            if (!reactorDiscovered) {
                File root = session == null ? pomFile.toFile() : session.getRequest().getPom();
                discoverReactor(root == null ? pomFile : root.toPath(), new HashSet<Path>(), session);
                reactorDiscovered = true;
            }
            try {
                VersionFileResolver.findNearest(projectDirectory);
            } catch (AdapterException noVersionConfiguration) {
                // Maven also sends dependency and external parent POMs through this global hook.
                return model;
            }

            AdapterVersion adapter =
                    calculator.calculate(projectDirectory, System.getProperty("nbgv.executable"));
            applyVersion(model, adapter);
            stampRelativeParent(model, projectDirectory, session);
            updateReactorReferences(model, prepareModel(model, projectDirectory, session));

            return model;
        } catch (AdapterException e) {
            throw new IOException(
                    "Nerdbank.GitVersioning could not stamp Maven model '" + pomFile + "': " + e.getMessage(), e);
        }
    }

    private void discoverReactor(Path pom, Set<Path> visited, MavenSession session) throws IOException {
        pom = pom.toRealPath();
        if (!visited.add(pom)) {
            return;
        }

        Path directory = pom.getParent();
        Model model = prepareModel(requireReader().read(pom.toFile(), null), directory, session);
        model.setPomFile(pom.toFile());

        boolean configured;
        try {
            VersionFileResolver.findNearest(directory);
            configured = true;
        } catch (AdapterException noVersionConfiguration) {
            configured = false;
        }
        if (configured) {
            AdapterVersion version = calculator.calculate(directory, System.getProperty("nbgv.executable"));
            reactorVersions.put(coordinates(groupId(model), model.getArtifactId()), version.getSemVer2());
        }
        if (session != null && !session.getRequest().isRecursive()) {
            return;
        }
        for (String module : model.getModules()) {
            File modulePath = directory.resolve(module).toFile();
            File modulePom = modulePath.isFile() ? modulePath : requireLocator().locatePom(modulePath);
            if (modulePom == null || !modulePom.isFile()) {
                throw new IOException("Cannot locate reactor module '" + module + "' in " + pom + ".");
            }
            discoverReactor(modulePom.toPath(), visited, session);
        }
    }

    private Model prepareModel(Model raw, Path directory, MavenSession session) throws IOException {
        Model model = raw.clone();
        Properties properties = inheritedProperties(model, directory, new HashSet<Path>(), session);
        properties.putAll(model.getProperties());
        model.setProperties(properties);
        List<Profile> activeProfiles = activeProfiles(model, directory, session);
        for (Profile profile : activeProfiles) {
            properties.putAll(profile.getProperties());
            model.getModules().addAll(profile.getModules());
        }

        if (model.getGroupId() == null && model.getParent() != null) {
            model.setGroupId(model.getParent().getGroupId());
        }
        if (interpolator != null) {
            DefaultModelBuildingRequest request = new DefaultModelBuildingRequest()
                    .setSystemProperties(session == null ? System.getProperties() : session.getSystemProperties())
                    .setUserProperties(session == null ? new Properties() : session.getUserProperties());
            model = interpolator.interpolateModel(model, directory.toFile(), request, problems());
        }

        return model;
    }

    private Properties inheritedProperties(
            Model model, Path directory, Set<Path> visited, MavenSession session) throws IOException {
        Properties properties = new Properties();
        Path parentPom = relativeParent(model, directory, session);
        if (parentPom != null && visited.add(parentPom.toRealPath())) {
            Model parent = requireReader().read(parentPom.toFile(), null);
            properties.putAll(inheritedProperties(parent, parentPom.getParent(), visited, session));
            properties.putAll(parent.getProperties());
            parent.setProperties(properties);
            for (Profile profile : activeProfiles(parent, parentPom.getParent(), session)) {
                properties.putAll(profile.getProperties());
            }
        }
        return properties;
    }

    private List<Profile> activeProfiles(Model model, Path directory, MavenSession session) {
        if (profileSelector == null) {
            return java.util.Collections.emptyList();
        }
        DefaultProfileActivationContext context = new DefaultProfileActivationContext()
                .setProjectDirectory(directory.toFile())
                .setProjectProperties(model.getProperties())
                .setSystemProperties(session == null ? System.getProperties() : session.getSystemProperties());
        if (session != null) {
            context.setUserProperties(session.getUserProperties())
                    .setActiveProfileIds(session.getRequest().getActiveProfiles())
                    .setInactiveProfileIds(session.getRequest().getInactiveProfiles());
        }
        List<Profile> profiles = new ArrayList<Profile>(
                profileSelector.getActiveProfiles(model.getProfiles(), context, problems()));
        if (session != null) {
            profiles.addAll(profileSelector.getActiveProfiles(session.getRequest().getProfiles(), context, problems()));
        }
        return profiles;
    }

    private static ModelProblemCollector problems() {
        return problem -> {
            if (problem.getSeverity() == ModelProblem.Severity.ERROR
                    || problem.getSeverity() == ModelProblem.Severity.FATAL) {
                throw new AdapterException(problem.getMessage(), problem.getException());
            }
            LoggerFactory.getLogger(NbgvModelProcessor.class).warn(problem.getMessage());
        };
    }

    private Path relativeParent(Model model, Path projectDirectory, MavenSession session) throws IOException {
        Parent parent = model.getParent();
        if (parent == null || "".equals(parent.getRelativePath())) {
            return null;
        }
        String relativePath = parent.getRelativePath() == null ? "../pom.xml" : parent.getRelativePath();
        Path path = projectDirectory.resolve(relativePath).normalize();
        if (path.toFile().isDirectory()) {
            path = path.resolve("pom.xml");
        }
        if (!path.toFile().isFile()) {
            return null;
        }
        Model local = requireReader().read(path.toFile(), null);
        Model child = model.clone();
        if (interpolator != null) {
            Properties properties = new Properties();
            properties.putAll(local.getProperties());
            properties.putAll(child.getProperties());
            child.setProperties(properties);
            DefaultModelBuildingRequest request = new DefaultModelBuildingRequest()
                    .setSystemProperties(session == null ? System.getProperties() : session.getSystemProperties())
                    .setUserProperties(session == null ? new Properties() : session.getUserProperties());
            child = interpolator.interpolateModel(child, projectDirectory.toFile(), request, problems());
            local = interpolator.interpolateModel(local, path.getParent().toFile(), request, problems());
        }
        Parent declared = child.getParent();
        return java.util.Objects.equals(declared.getGroupId(), groupId(local))
                        && java.util.Objects.equals(declared.getArtifactId(), local.getArtifactId())
                ? path : null;
    }

    private void stampRelativeParent(Model model, Path projectDirectory, MavenSession session) throws IOException {
        Parent parent = model.getParent();
        Path parentPath = relativeParent(model, projectDirectory, session);
        if (parentPath == null) {
            return;
        }

        try {
            VersionFileResolver.findNearest(parentPath.getParent());
        } catch (AdapterException noParentVersionConfiguration) {
            return;
        }

        AdapterVersion parentVersion =
                calculator.calculate(parentPath.getParent(), System.getProperty("nbgv.executable"));
        parent.setVersion(parentVersion.getSemVer2());
    }

    private void updateReactorReferences(Model model, Model resolved) {
        Parent parent = model.getParent();
        if (parent != null) {
            String version = reactorVersions.get(
                    coordinates(resolved.getParent().getGroupId(), resolved.getParent().getArtifactId()));
            if (version != null) {
                parent.setVersion(version);
            }
        }

        updateDependencies(model.getDependencies(), resolved.getDependencies());
        DependencyManagement management = model.getDependencyManagement();
        if (management != null) {
            updateDependencies(management.getDependencies(), resolved.getDependencyManagement().getDependencies());
        }
        for (int index = 0; index < model.getProfiles().size(); index++) {
            Profile profile = model.getProfiles().get(index);
            Profile resolvedProfile = resolved.getProfiles().get(index);
            updateDependencies(profile.getDependencies(), resolvedProfile.getDependencies());
            if (profile.getDependencyManagement() != null) {
                updateDependencies(
                        profile.getDependencyManagement().getDependencies(),
                        resolvedProfile.getDependencyManagement().getDependencies());
            }
        }
    }

    private void updateDependencies(List<Dependency> dependencies, List<Dependency> resolved) {
        for (int index = 0; index < dependencies.size(); index++) {
            Dependency dependency = dependencies.get(index);
            Dependency identity = resolved.get(index);
            String version =
                    reactorVersions.get(coordinates(identity.getGroupId(), identity.getArtifactId()));
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
