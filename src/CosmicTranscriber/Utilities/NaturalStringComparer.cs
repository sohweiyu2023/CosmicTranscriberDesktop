namespace CosmicTranscriber.Utilities;

/// <summary>
/// Compares strings in human/natural order so numbered parts sort as 1, 2, 10
/// instead of 1, 10, 2. Comparison is ordinal and case-insensitive outside digits.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xIndex = 0;
        var yIndex = 0;
        while (xIndex < x.Length && yIndex < y.Length)
        {
            var xDigit = char.IsDigit(x[xIndex]);
            var yDigit = char.IsDigit(y[yIndex]);

            if (xDigit && yDigit)
            {
                var xStart = xIndex;
                var yStart = yIndex;
                while (xIndex < x.Length && char.IsDigit(x[xIndex])) xIndex++;
                while (yIndex < y.Length && char.IsDigit(y[yIndex])) yIndex++;

                var xNonZero = xStart;
                var yNonZero = yStart;
                while (xNonZero < xIndex && x[xNonZero] == '0') xNonZero++;
                while (yNonZero < yIndex && y[yNonZero] == '0') yNonZero++;

                var xDigits = xIndex - xNonZero;
                var yDigits = yIndex - yNonZero;
                if (xDigits != yDigits)
                {
                    return xDigits.CompareTo(yDigits);
                }

                for (var i = 0; i < xDigits; i++)
                {
                    var digitComparison = x[xNonZero + i].CompareTo(y[yNonZero + i]);
                    if (digitComparison != 0)
                    {
                        return digitComparison;
                    }
                }

                // Numerically equal: place the shorter representation first (2 before 002).
                var representationComparison = (xIndex - xStart).CompareTo(yIndex - yStart);
                if (representationComparison != 0)
                {
                    return representationComparison;
                }

                continue;
            }

            var xChar = char.ToUpperInvariant(x[xIndex]);
            var yChar = char.ToUpperInvariant(y[yIndex]);
            var comparison = xChar.CompareTo(yChar);
            if (comparison != 0)
            {
                return comparison;
            }

            xIndex++;
            yIndex++;
        }

        return (x.Length - xIndex).CompareTo(y.Length - yIndex);
    }
}
