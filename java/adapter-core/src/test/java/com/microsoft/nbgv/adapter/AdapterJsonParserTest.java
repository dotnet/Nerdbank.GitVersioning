package com.microsoft.nbgv.adapter;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.Assertions.assertThrows;

import org.junit.jupiter.api.Test;

class AdapterJsonParserTest {
    private static final String VALID_JSON = "{"
            + "\"schemaVersion\":1,"
            + "\"semVer2\":\"1.2.3-beta.4+abc\","
            + "\"version\":\"1.2.3.4\","
            + "\"versionHeight\":4,"
            + "\"gitCommitId\":\"0123456789abcdef\","
            + "\"gitCommitIdShort\":\"01234567\","
            + "\"gitCommitDate\":\"2026-08-09T21:59:55Z\","
            + "\"publicRelease\":false"
            + "}";

    @Test
    void parsesContractAndIgnoresUnknownProperties() {
        AdapterVersion result = new AdapterJsonParser().parse(
                VALID_JSON.substring(0, VALID_JSON.length() - 1) + ",\"futureProperty\":{\"value\":1}}");

        assertEquals(1, result.getSchemaVersion());
        assertEquals("1.2.3-beta.4+abc", result.getSemVer2());
        assertEquals("01234567", result.getGitCommitIdShort().get());
        assertFalse(result.isPublicRelease());
    }

    @Test
    void permitsIndependentlyUnavailableGitValuesAndEmptyShortId() {
        AdapterJsonParser parser = new AdapterJsonParser();
        AdapterVersion cloudValue = parser.parse(VALID_JSON.replace(
                "\"gitCommitDate\":\"2026-08-09T21:59:55Z\"", "\"gitCommitDate\":null"));
        assertEquals("0123456789abcdef", cloudValue.getGitCommitId().get());
        assertFalse(cloudValue.getGitCommitDate().isPresent());

        AdapterVersion emptyShortId = parser.parse(VALID_JSON.replace(
                "\"gitCommitIdShort\":\"01234567\"", "\"gitCommitIdShort\":\"\""));
        assertTrue(emptyShortId.getGitCommitIdShort().isPresent());
        assertEquals("", emptyShortId.getGitCommitIdShort().get());

        AdapterVersion independentNulls = parser.parse(VALID_JSON
                .replace("\"gitCommitId\":\"0123456789abcdef\"", "\"gitCommitId\":null")
                .replace("\"gitCommitIdShort\":\"01234567\"", "\"gitCommitIdShort\":null"));
        assertFalse(independentNulls.getGitCommitId().isPresent());
        assertTrue(independentNulls.getGitCommitDate().isPresent());
    }

    @Test
    void permitsDirtyCommitIds() {
        String json = VALID_JSON.replace("\"gitCommitId\":\"0123456789abcdef\"", "\"gitCommitId\":\"0123456789abcdef-dirty\"")
                .replace("\"gitCommitIdShort\":\"01234567\"", "\"gitCommitIdShort\":\"01234567-dirty\"");
        AdapterVersion result = new AdapterJsonParser().parse(json);
        assertEquals("01234567-dirty", result.getGitCommitIdShort().get());
        result = new AdapterJsonParser().parse(json.replace("01234567-dirty", "-dirty"));
        assertEquals("-dirty", result.getGitCommitIdShort().get());
    }

    @Test
    void rejectsUnsupportedSchemaBeforeOtherValues() {
        String json = VALID_JSON.replace("\"schemaVersion\":1", "\"schemaVersion\":2")
                .replace("\"semVer2\":\"1.2.3-beta.4+abc\"", "\"semVer2\":false");
        AdapterException exception = assertThrows(AdapterException.class, () -> new AdapterJsonParser().parse(json));
        assertEquals("Unsupported nbgv adapter schemaVersion 2; expected 1.", exception.getMessage());
    }

    @Test
    void rejectsMissingWrongTypeAndTrailingJson() {
        AdapterJsonParser parser = new AdapterJsonParser();
        assertThrows(AdapterException.class, () -> parser.parse(VALID_JSON.replace("\"versionHeight\":4,", "")));
        assertThrows(AdapterException.class, () -> parser.parse(VALID_JSON.replace("\"publicRelease\":false", "\"publicRelease\":\"false\"")));
        assertThrows(AdapterException.class, () -> parser.parse(VALID_JSON + "{}"));
        assertThrows(
                AdapterException.class,
                () -> parser.parse(VALID_JSON.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")));
    }
}
