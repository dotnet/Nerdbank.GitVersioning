package com.microsoft.nbgv.adapter;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.time.Instant;
import java.time.format.DateTimeParseException;
import java.util.regex.Pattern;

/**
 * Strictly validates the versioned adapter JSON contract.
 */
public final class AdapterJsonParser {
    private static final Pattern SEMVER = Pattern.compile(
            "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)"
                    + "(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
                    + "(?:\\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?"
                    + "(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$");
    private static final Pattern NUMERIC_VERSION =
            Pattern.compile("^[0-9]+\\.[0-9]+(?:\\.[0-9]+(?:\\.[0-9]+)?)?$");
    private static final Pattern OBJECT_ID = Pattern.compile("^[0-9a-f]+$");
    private static final Pattern SHORT_OBJECT_ID = Pattern.compile("^[0-9a-f]*$");
    private static final Pattern UTC_WHOLE_SECONDS =
            Pattern.compile("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$");

    private final ObjectMapper objectMapper = new ObjectMapper()
            .enable(JsonParser.Feature.STRICT_DUPLICATE_DETECTION)
            .enable(DeserializationFeature.FAIL_ON_TRAILING_TOKENS);

    /**
     * Parses and validates adapter JSON. Unknown properties are intentionally ignored.
     *
     * @param json the complete standard output from nbgv
     * @return the validated immutable value
     */
    public AdapterVersion parse(String json) {
        final JsonNode root;
        try {
            root = objectMapper.readTree(json);
        } catch (JsonProcessingException e) {
            throw new AdapterException("nbgv returned malformed adapter JSON: " + e.getOriginalMessage(), e);
        }

        if (root == null || !root.isObject()) {
            throw new AdapterException("nbgv adapter output must be one JSON object.");
        }

        int schemaVersion = requiredInt(root, "schemaVersion");
        if (schemaVersion != 1) {
            throw new AdapterException("Unsupported nbgv adapter schemaVersion " + schemaVersion + "; expected 1.");
        }

        String semVer2 = requiredString(root, "semVer2");
        if (!SEMVER.matcher(semVer2).matches()) {
            throw new AdapterException("Adapter property 'semVer2' is not a valid SemVer 2.0 version.");
        }

        String version = requiredString(root, "version");
        if (!NUMERIC_VERSION.matcher(version).matches()) {
            throw new AdapterException("Adapter property 'version' must contain two to four numeric components.");
        }

        int versionHeight = requiredInt(root, "versionHeight");
        if (versionHeight < 0) {
            throw new AdapterException("Adapter property 'versionHeight' must not be negative.");
        }

        String gitCommitId = nullableObjectId(root, "gitCommitId", false);
        String gitCommitIdShort = nullableObjectId(root, "gitCommitIdShort", true);
        Instant gitCommitDate = nullableInstant(root, "gitCommitDate");
        if (gitCommitId != null && gitCommitIdShort != null && !gitCommitId.startsWith(gitCommitIdShort)) {
            throw new AdapterException("Adapter property 'gitCommitIdShort' is not a prefix of 'gitCommitId'.");
        }

        boolean publicRelease = requiredBoolean(root, "publicRelease");

        return new AdapterVersion(
                schemaVersion,
                semVer2,
                version,
                versionHeight,
                gitCommitId,
                gitCommitIdShort,
                gitCommitDate,
                publicRelease);
    }

    private static JsonNode required(JsonNode root, String property) {
        JsonNode value = root.get(property);
        if (value == null) {
            throw new AdapterException("Adapter JSON is missing required property '" + property + "'.");
        }

        return value;
    }

    private static int requiredInt(JsonNode root, String property) {
        JsonNode value = required(root, property);
        if (!value.isIntegralNumber() || !value.canConvertToInt()) {
            throw wrongType(property, "integer");
        }

        return value.intValue();
    }

    private static String requiredString(JsonNode root, String property) {
        JsonNode value = required(root, property);
        if (!value.isTextual()) {
            throw wrongType(property, "string");
        }

        return value.textValue();
    }

    private static boolean requiredBoolean(JsonNode root, String property) {
        JsonNode value = required(root, property);
        if (!value.isBoolean()) {
            throw wrongType(property, "boolean");
        }

        return value.booleanValue();
    }

    private static String nullableObjectId(JsonNode root, String property, boolean allowEmpty) {
        JsonNode value = required(root, property);
        if (value.isNull()) {
            return null;
        }

        if (!value.isTextual()) {
            throw wrongType(property, "string or null");
        }

        String text = value.textValue();
        Pattern pattern = allowEmpty ? SHORT_OBJECT_ID : OBJECT_ID;
        if (!pattern.matcher(text).matches()) {
            throw new AdapterException("Adapter property '" + property + "' must be lowercase hexadecimal.");
        }

        return text;
    }

    private static Instant nullableInstant(JsonNode root, String property) {
        JsonNode value = required(root, property);
        if (value.isNull()) {
            return null;
        }

        if (!value.isTextual()) {
            throw wrongType(property, "string or null");
        }

        String text = value.textValue();
        if (!UTC_WHOLE_SECONDS.matcher(text).matches()) {
            throw new AdapterException(
                    "Adapter property '" + property + "' must be an RFC 3339 UTC timestamp in whole seconds.");
        }

        try {
            return Instant.parse(text);
        } catch (DateTimeParseException e) {
            throw new AdapterException("Adapter property '" + property + "' is not a valid timestamp.", e);
        }
    }

    private static AdapterException wrongType(String property, String expected) {
        return new AdapterException("Adapter property '" + property + "' must be a JSON " + expected + ".");
    }
}
