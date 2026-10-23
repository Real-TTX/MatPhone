using System.Text;

namespace MatPhone.Core.Telephony;

/// <summary>How numbers are written in this installation: the country, the area and the prefix for an outside line.</summary>
public sealed record NumberPlan(string CountryCode = "49", string AreaCode = "", string OutsideLinePrefix = "0");

/// <summary>
/// Reading and writing phone numbers. Inside the installation an external number is always E.164 with a plus ("+4930123456"); an extension is digits ("201").
/// What a person types or a provider sends ("030 123456", "0049 30 123456", "+49 (0)30 123456", "123456") becomes that, and a line gets the number in the format
/// its provider wants.
/// </summary>
public static class NumberRules
{
    /// <summary>Only what can be dialled: digits, a leading plus, the star and the hash. Spaces, dashes, dots, slashes and brackets go.</summary>
    public static string Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = input.Trim();
        // "+49 (0)30 ..." is a common way to write a number: the zero in brackets is not dialled after the country code.
        text = text.Replace("(0)", string.Empty, StringComparison.Ordinal);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c) || c is '*' or '#')
            {
                sb.Append(c);
            }
            else if (c == '+' && sb.Length == 0)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>An extension: digits only, at most 8 of them (a longer number is an outside number).</summary>
    public static bool IsExtension(string? number)
        => !string.IsNullOrEmpty(number) && number.Length <= 8 && number.All(char.IsAsciiDigit);

    /// <summary>A feature code starts with a star ("*86").</summary>
    public static bool IsFeatureCode(string? number) => !string.IsNullOrEmpty(number) && number[0] == '*';

    /// <summary>
    /// An external number as E.164 with a plus, or null when it is not one: "+4930123" stays, "004930123" and "030123" (national) and "123456" (local, with the
    /// area code of the plan) become "+4930123...". The prefix of the outside line is not part of it (see <see cref="StripOutsideLine"/>).
    /// </summary>
    public static string? ToE164(string? input, NumberPlan plan)
    {
        var n = Clean(input);
        if (n.Length == 0 || n.Contains('*') || n.Contains('#'))
        {
            return null;
        }

        if (n.StartsWith('+'))
        {
            return n.Length > 4 ? n : null;
        }

        if (n.StartsWith("00", StringComparison.Ordinal))
        {
            return n.Length > 5 ? "+" + n[2..] : null;
        }

        if (n.StartsWith('0'))
        {
            return n.Length > 3 ? "+" + plan.CountryCode + n[1..] : null;
        }

        // A local number: only with an area code, and only when it is long enough not to be an extension typed by mistake.
        return plan.AreaCode.Length > 0 && n.Length >= 5 ? "+" + plan.CountryCode + plan.AreaCode.TrimStart('0') + n : null;
    }

    /// <summary>What remains of a dialled number after the prefix of the outside line ("0" + "0301234" = "0301234"), or null when it does not start with it.</summary>
    public static string? StripOutsideLine(string dialled, NumberPlan plan)
    {
        if (plan.OutsideLinePrefix.Length == 0)
        {
            return dialled;
        }

        return dialled.StartsWith(plan.OutsideLinePrefix, StringComparison.Ordinal) && dialled.Length > plan.OutsideLinePrefix.Length
            ? dialled[plan.OutsideLinePrefix.Length..]
            : null;
    }

    /// <summary>An E.164 number in the format a provider wants in the request line.</summary>
    public static string Format(string e164, NumberFormatKind format, NumberPlan plan)
    {
        if (!e164.StartsWith('+'))
        {
            return e164;
        }

        return format switch
        {
            NumberFormatKind.E164 => e164[1..],
            NumberFormatKind.National when e164.StartsWith("+" + plan.CountryCode, StringComparison.Ordinal) => "0" + e164[(plan.CountryCode.Length + 1)..],
            NumberFormatKind.National => "00" + e164[1..],
            _ => e164,
        };
    }

    /// <summary>
    /// A number as people read it: "+49 30 1234567" for E.164 of the own country gets the national form "030 1234567"; others keep the plus. Extensions stay.
    /// Only for display: the groups are a guess (the area code is not known for every number).
    /// </summary>
    public static string Display(string? number, NumberPlan plan)
    {
        if (string.IsNullOrEmpty(number) || !number.StartsWith('+'))
        {
            return number ?? string.Empty;
        }

        var own = "+" + plan.CountryCode;
        if (number.StartsWith(own, StringComparison.Ordinal))
        {
            var national = number[own.Length..];
            if (plan.AreaCode.Length > 0 && national.StartsWith(plan.AreaCode.TrimStart('0'), StringComparison.Ordinal))
            {
                var area = plan.AreaCode.TrimStart('0');
                return "0" + area + " " + national[area.Length..];
            }

            return "0" + national;
        }

        return number;
    }

    /// <summary>The digits of a number for comparing two ways of writing it ("+49301234" and "0301234" are the same caller).</summary>
    public static bool SameNumber(string? a, string? b, NumberPlan plan)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        var x = ToE164(a, plan) ?? Clean(a);
        var y = ToE164(b, plan) ?? Clean(b);
        return x == y;
    }
}

/// <summary>The formats of <see cref="NumberRules.Format"/> (the same names as the enum of the data model).</summary>
public enum NumberFormatKind
{
    E164Plus,
    E164,
    National,
}
