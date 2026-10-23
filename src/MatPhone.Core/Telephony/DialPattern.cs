namespace MatPhone.Core.Telephony;

/// <summary>
/// The patterns of the rules ("0900.", "+49151.", "11X", "0[1-9]."): <c>X</c> is any digit, <c>Z</c> 1 to 9, <c>N</c> 2 to 9, <c>[1-5]</c> or <c>[137]</c>
/// one digit of the set, a dot at the end one or more of anything, an exclamation mark at the end zero or more. Every other character stands for itself
/// (digits, <c>+</c>, <c>*</c>, <c>#</c>). An empty pattern matches every number.
/// </summary>
public static class DialPattern
{
    /// <summary>Whether the pattern uses only what <see cref="Matches"/> knows (a dot or an exclamation mark only at the end, closed brackets of digits).</summary>
    public static bool IsValid(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        var p = pattern.Trim();
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (c == '[')
            {
                var end = p.IndexOf(']', i + 1);
                if (end < 0 || end == i + 1 || !p[(i + 1)..end].All(ch => char.IsAsciiDigit(ch) || ch == '-'))
                {
                    return false;
                }

                i = end;
                continue;
            }

            if (c is '.' or '!')
            {
                if (i != p.Length - 1)
                {
                    return false;
                }

                continue;
            }

            if (!(char.IsAsciiDigit(c) || c is '+' or '*' or '#' or 'X' or 'x' or 'Z' or 'z' or 'N' or 'n'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool Matches(string? pattern, string? number)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        if (string.IsNullOrEmpty(number))
        {
            return false;
        }

        var p = pattern.Trim();
        var n = 0;
        for (var i = 0; i < p.Length; i++)
        {
            var c = char.ToUpperInvariant(p[i]);
            if (c == '.')
            {
                return n < number.Length;
            }

            if (c == '!')
            {
                return true;
            }

            if (n >= number.Length)
            {
                return false;
            }

            var d = number[n];
            if (c == '[')
            {
                var end = p.IndexOf(']', i + 1);
                if (end < 0 || !InSet(p[(i + 1)..end], d))
                {
                    return false;
                }

                i = end;
                n++;
                continue;
            }

            var ok = c switch
            {
                'X' => char.IsAsciiDigit(d),
                'Z' => d is >= '1' and <= '9',
                'N' => d is >= '2' and <= '9',
                _ => d == c,
            };
            if (!ok)
            {
                return false;
            }

            n++;
        }

        return n == number.Length;
    }

    private static bool InSet(string set, char d)
    {
        for (var i = 0; i < set.Length; i++)
        {
            if (i + 2 < set.Length && set[i + 1] == '-')
            {
                if (d >= set[i] && d <= set[i + 2])
                {
                    return true;
                }

                i += 2;
                continue;
            }

            if (set[i] == d)
            {
                return true;
            }
        }

        return false;
    }
}
