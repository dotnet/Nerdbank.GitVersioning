package com.microsoft.nbgv.adapter;

/**
 * Indicates that version calculation or adapter JSON validation failed.
 */
public final class AdapterException extends RuntimeException {
    /**
     * Initializes an exception.
     *
     * @param message a diagnostic suitable for a build log
     */
    public AdapterException(String message) {
        super(message);
    }

    /**
     * Initializes an exception.
     *
     * @param message a diagnostic suitable for a build log
     * @param cause the underlying failure
     */
    public AdapterException(String message, Throwable cause) {
        super(message, cause);
    }
}
