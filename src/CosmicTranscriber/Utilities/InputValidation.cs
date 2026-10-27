namespace CosmicTranscriber.Utilities;

public static class InputValidation
{
    public const int MaximumApiKeyCharacters = 512;
    public const int MaximumBasePromptCharacters = 12_000;
    public const int MaximumFinalPromptCharacters = 16_000;
    public const int MaximumCombinedTitleCharacters = 300;
    public const int MaximumOutputPathCharacters = 2_048;
    public const int MaximumPriceCharacters = 64;

    public static string NormalizeApiKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var key = value.Trim();
        if (key.Length == 0)
        {
            throw new ArgumentException("The OpenAI API key is empty.", nameof(value));
        }

        if (key.Length > MaximumApiKeyCharacters)
        {
            throw new ArgumentException("The OpenAI API key is unexpectedly long.", nameof(value));
        }

        if (key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
        {
            throw new ArgumentException("The OpenAI API key contains whitespace or control characters.", nameof(value));
        }

        return key;
    }

    public const int MaximumLanguageHintCharacters = 128;
    public const int MaximumLanguageHints = 16;
    public const int MaximumKeywordInputCharacters = 8_000;
    public const int MaximumKeywordCharacters = 200;
    public const int MaximumKeywords = 200;

    private static readonly char[] LanguageSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    public static IReadOnlyList<string> NormalizeLanguages(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        if (value.Length > MaximumLanguageHintCharacters)
        {
            throw new ArgumentException(
                $"Language guidance cannot exceed {MaximumLanguageHintCharacters} characters.",
                nameof(value));
        }

        var languages = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in value.Split(
                     LanguageSeparators,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var language = token.ToLowerInvariant();
            var isIsoCode = language.Length is 2 or 3
                            && language.All(ch => ch is >= 'a' and <= 'z');
            var isRegionalChinese = language is "zh-cn" or "zh-tw" or "zh-hk";
            if (!isIsoCode && !isRegionalChinese)
            {
                throw new ArgumentException(
                    "For gpt-transcribe, language guidance must use ISO 639-1 codes (en, zh), " +
                    "supported ISO 639-3 codes (eng, cmn, yue), or zh-cn/zh-tw/zh-hk. " +
                    "Separate multiple hints with commas.",
                    nameof(value));
            }

            if (seen.Add(language))
            {
                languages.Add(language);
                if (languages.Count > MaximumLanguageHints)
                {
                    throw new ArgumentException(
                        $"No more than {MaximumLanguageHints} language hints may be supplied.",
                        nameof(value));
                }
            }
        }

        return languages;
    }

    public static string NormalizeLanguageList(string? value)
        => string.Join(", ", NormalizeLanguages(value));

    public static string? NormalizeLanguage(string? value)
    {
        var languages = NormalizeLanguages(value);
        if (languages.Count > 1)
        {
            throw new ArgumentException(
                "The selected single-language transcription model accepts only one language hint.",
                nameof(value));
        }

        if (languages.Count == 0)
        {
            return null;
        }

        var language = languages[0];
        if (language.Length != 2 || language.Any(ch => ch is < 'a' or > 'z'))
        {
            throw new ArgumentException(
                "The selected single-language transcription model requires a two-letter ISO 639-1 hint such as en or zh.",
                nameof(value));
        }

        return language;
    }

    public static IReadOnlyList<string> NormalizeKeywords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        if (value.Length > MaximumKeywordInputCharacters)
        {
            throw new ArgumentException(
                $"Keyword guidance cannot exceed {MaximumKeywordInputCharacters:N0} characters.",
                nameof(value));
        }

        var keywords = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var keyword = rawLine.Trim();
            if (keyword.Length == 0)
            {
                continue;
            }

            if (keyword.Length > MaximumKeywordCharacters)
            {
                throw new ArgumentException(
                    $"Each keyword must be {MaximumKeywordCharacters} characters or fewer.",
                    nameof(value));
            }

            if (keyword.IndexOfAny(new[] { '<', '>', '\r', '\n' }) >= 0
                || keyword.Any(ch => char.IsControl(ch) || ch is '\u2028' or '\u2029'))
            {
                throw new ArgumentException(
                    "Each keyword must be a single-line literal without <, >, or control characters.",
                    nameof(value));
            }

            if (seen.Add(keyword))
            {
                keywords.Add(keyword);
                if (keywords.Count > MaximumKeywords)
                {
                    throw new ArgumentException(
                        $"No more than {MaximumKeywords} keyword hints may be supplied.",
                        nameof(value));
                }
            }
        }

        return keywords;
    }

    public static string NormalizeKeywordList(string? value)
        => string.Join(Environment.NewLine, NormalizeKeywords(value));

    public static string TruncateWithoutSplittingSurrogatePair(string value, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maximumCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        var length = maximumCharacters;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }


    public static string TakeTailWithoutSplittingSurrogatePair(string value, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maximumCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        var start = value.Length - maximumCharacters;
        if (start < value.Length && char.IsLowSurrogate(value[start]))
        {
            start++;
        }

        return value[start..];
    }

    public static string SingleLineForLog(string value, int maximumCharacters = 1_000)
    {
        ArgumentNullException.ThrowIfNull(value);
        var singleLine = new string(value
            .Select(ch => char.IsControl(ch) || ch is '\u2028' or '\u2029' ? ' ' : ch)
            .ToArray())
            .Trim();
        while (singleLine.Contains("  ", StringComparison.Ordinal))
        {
            singleLine = singleLine.Replace("  ", " ", StringComparison.Ordinal);
        }

        return TruncateWithoutSplittingSurrogatePair(singleLine, maximumCharacters);
    }
}
