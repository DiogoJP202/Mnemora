namespace Mnemora.Application;

public readonly record struct BookIsbnNormalization(
    bool IsValid, string? Isbn10, string? Isbn13)
{
    public bool HasValue => Isbn10 is not null || Isbn13 is not null;
    public string? Canonical => Isbn13 ?? Isbn10;
}

public static class BookIsbn
{
    public static BookIsbnNormalization Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new BookIsbnNormalization(true, null, null);
        if (value.Length > 32)
            return new BookIsbnNormalization(false, null, null);

        var compact = new string(value.Where(character =>
                !char.IsWhiteSpace(character) && character != '-').ToArray())
            .ToUpperInvariant();
        if (compact.Length == 10 && IsValidIsbn10(compact))
            return new BookIsbnNormalization(true, compact, ToIsbn13(compact));
        if (compact.Length == 13 && IsValidIsbn13(compact))
            return new BookIsbnNormalization(true,
                compact.StartsWith("978", StringComparison.Ordinal)
                    ? ToIsbn10(compact) : null,
                compact);
        return new BookIsbnNormalization(false, null, null);
    }

    private static bool IsValidIsbn10(string value)
    {
        if (value.Length != 10 || value[..9].Any(character => character is < '0' or > '9')
            || (value[9] is < '0' or > '9' && value[9] != 'X'))
            return false;
        var sum = 0;
        for (var index = 0; index < 10; index++)
        {
            var digit = index == 9 && value[index] == 'X' ? 10 : value[index] - '0';
            sum += (10 - index) * digit;
        }
        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string value)
    {
        if (value.Length != 13 || value.Any(character => character is < '0' or > '9'))
            return false;
        var sum = 0;
        for (var index = 0; index < 12; index++)
            sum += (value[index] - '0') * (index % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10 == value[12] - '0';
    }

    private static string ToIsbn13(string isbn10)
    {
        var body = $"978{isbn10[..9]}";
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
            sum += (body[index] - '0') * (index % 2 == 0 ? 1 : 3);
        return $"{body}{(10 - sum % 10) % 10}";
    }

    private static string ToIsbn10(string isbn13)
    {
        var body = isbn13.Substring(3, 9);
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
            sum += (body[index] - '0') * (10 - index);
        var check = (11 - sum % 11) % 11;
        return $"{body}{(check == 10 ? 'X' : (char)('0' + check))}";
    }
}
