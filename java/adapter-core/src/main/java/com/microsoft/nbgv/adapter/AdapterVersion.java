package com.microsoft.nbgv.adapter;

import java.io.Serializable;
import java.time.Instant;
import java.util.Objects;
import java.util.Optional;

/**
 * An immutable value returned by {@code nbgv get-version --format adapter}.
 */
public final class AdapterVersion implements Serializable {
    private static final long serialVersionUID = 1L;

    private final int schemaVersion;
    private final String semVer2;
    private final String version;
    private final int versionHeight;
    private final String gitCommitId;
    private final String gitCommitIdShort;
    private final String gitCommitDate;
    private final boolean publicRelease;

    AdapterVersion(
            int schemaVersion,
            String semVer2,
            String version,
            int versionHeight,
            String gitCommitId,
            String gitCommitIdShort,
            Instant gitCommitDate,
            boolean publicRelease) {
        this.schemaVersion = schemaVersion;
        this.semVer2 = Objects.requireNonNull(semVer2, "semVer2");
        this.version = Objects.requireNonNull(version, "version");
        this.versionHeight = versionHeight;
        this.gitCommitId = gitCommitId;
        this.gitCommitIdShort = gitCommitIdShort;
        this.gitCommitDate = gitCommitDate == null ? null : gitCommitDate.toString();
        this.publicRelease = publicRelease;
    }

    /** Gets the adapter schema version. */
    public int getSchemaVersion() {
        return schemaVersion;
    }

    /** Gets the complete immutable SemVer 2.0 version. */
    public String getSemVer2() {
        return semVer2;
    }

    /** Gets the numeric two-to-four-component version. */
    public String getVersion() {
        return version;
    }

    /** Gets the Git version height before applying the configured offset. */
    public int getVersionHeight() {
        return versionHeight;
    }

    /** Gets the full Git object ID, when available. */
    public Optional<String> getGitCommitId() {
        return Optional.ofNullable(gitCommitId);
    }

    /** Gets the abbreviated Git object ID, when available. */
    public Optional<String> getGitCommitIdShort() {
        return Optional.ofNullable(gitCommitIdShort);
    }

    /** Gets the UTC commit timestamp, when available. */
    public Optional<Instant> getGitCommitDate() {
        return gitCommitDate == null ? Optional.<Instant>empty() : Optional.of(Instant.parse(gitCommitDate));
    }

    /** Gets whether public release mode is active. */
    public boolean isPublicRelease() {
        return publicRelease;
    }

    /** Compares all schema-defined values. */
    @Override
    public boolean equals(Object obj) {
        if (this == obj) {
            return true;
        }

        if (!(obj instanceof AdapterVersion)) {
            return false;
        }

        AdapterVersion other = (AdapterVersion) obj;
        return schemaVersion == other.schemaVersion
                && versionHeight == other.versionHeight
                && publicRelease == other.publicRelease
                && semVer2.equals(other.semVer2)
                && version.equals(other.version)
                && Objects.equals(gitCommitId, other.gitCommitId)
                && Objects.equals(gitCommitIdShort, other.gitCommitIdShort)
                && Objects.equals(gitCommitDate, other.gitCommitDate);
    }

    /** Returns a hash over all schema-defined values. */
    @Override
    public int hashCode() {
        return Objects.hash(
                schemaVersion,
                semVer2,
                version,
                versionHeight,
                gitCommitId,
                gitCommitIdShort,
                gitCommitDate,
                publicRelease);
    }
}
