package com.microsoft.nbgv.gradle;

import org.gradle.api.Action;
import org.gradle.api.provider.Property;

/**
 * Configures the Nerdbank.GitVersioning Gradle plugin.
 */
public final class NbgvExtension {
    private final Property<String> executable;
    private final Action<String> executableChanged;

    /**
     * Initializes the extension.
     *
     * @param executable executable property
     * @param executableChanged action that eagerly refreshes the project version
     */
    public NbgvExtension(Property<String> executable, Action<String> executableChanged) {
        this.executable = executable;
        this.executableChanged = executableChanged;
    }

    /**
     * Gets the explicitly selected nbgv executable path or command name.
     *
     * @return executable property
     */
    public Property<String> getExecutable() {
        return executable;
    }

    /**
     * Selects an executable and immediately makes its calculated version the project version.
     *
     * @param executable one executable path or command name
     */
    public void setExecutable(String executable) {
        getExecutable().set(executable);
        executableChanged.execute(executable);
    }

    /**
     * Selects an executable and immediately makes its calculated version the project version.
     *
     * @param executable one executable path or command name
     */
    public void executable(String executable) {
        setExecutable(executable);
    }
}
