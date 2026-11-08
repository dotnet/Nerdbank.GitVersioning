package com.microsoft.nbgv.adapter;

import java.util.Map;
import java.util.Objects;

/**
 * Applies the documented executable resolution policy.
 */
public final class NbgvExecutableResolver {
    private NbgvExecutableResolver() {
    }

    /**
     * Resolves the executable as explicit configuration, {@code NBGV_EXECUTABLE}, then {@code nbgv}.
     *
     * @param explicitExecutable explicitly configured executable, or {@code null}
     * @param environment process environment
     * @return one executable path or command name
     */
    public static String resolve(String explicitExecutable, Map<String, String> environment) {
        Objects.requireNonNull(environment, "environment");
        if (explicitExecutable != null) {
            return requireUsable(explicitExecutable, "The explicitly configured nbgv executable");
        }

        String fromEnvironment = environment.get("NBGV_EXECUTABLE");
        if (fromEnvironment != null) {
            return requireUsable(fromEnvironment, "NBGV_EXECUTABLE");
        }

        return "nbgv";
    }

    private static String requireUsable(String value, String source) {
        if (value.trim().isEmpty()) {
            throw new AdapterException(source + " must not be empty.");
        }

        if (value.indexOf('\0') >= 0) {
            throw new AdapterException(source + " contains an invalid null character.");
        }

        return value;
    }
}
