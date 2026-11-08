package com.microsoft.nbgv.gradle;

import com.microsoft.nbgv.adapter.AdapterCalculator;
import com.microsoft.nbgv.adapter.AdapterVersion;
import javax.annotation.Nullable;
import org.gradle.api.file.DirectoryProperty;
import org.gradle.api.file.RegularFileProperty;
import org.gradle.api.provider.Property;
import org.gradle.api.provider.ValueSource;
import org.gradle.api.provider.ValueSourceParameters;
import org.gradle.api.tasks.Input;
import org.gradle.api.tasks.InputDirectory;
import org.gradle.api.tasks.InputFile;
import org.gradle.api.tasks.Optional;
import org.gradle.api.tasks.PathSensitive;
import org.gradle.api.tasks.PathSensitivity;

/**
 * Obtains adapter data through Gradle's configuration-cache-aware ValueSource API.
 */
public abstract class NbgvValueSource implements ValueSource<AdapterVersion, NbgvValueSource.Parameters> {
    /** Parameters that identify a version calculation. */
    public interface Parameters extends ValueSourceParameters {
        /** Gets the project directory in which version resolution begins. */
        @InputDirectory
        @PathSensitive(PathSensitivity.RELATIVE)
        DirectoryProperty getProjectDirectory();

        /** Gets the nearest version.json, so Gradle tracks configuration changes. */
        @InputFile
        @PathSensitive(PathSensitivity.RELATIVE)
        RegularFileProperty getVersionJson();

        /** Gets the optional explicit executable. */
        @Input
        @Optional
        Property<String> getExecutable();
    }

    /** Calculates the value once for these parameters. */
    @Override
    @Nullable
    public AdapterVersion obtain() {
        String executable = getParameters().getExecutable().getOrNull();
        return new AdapterCalculator().calculate(
                getParameters().getProjectDirectory().getAsFile().get().toPath(), executable);
    }
}
