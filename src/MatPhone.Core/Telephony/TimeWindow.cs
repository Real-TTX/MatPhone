namespace MatPhone.Core.Telephony;

/// <summary>
/// When a rule applies: days of the week as bits (bit 0 = Sunday ... bit 6 = Saturday, 0 = every day) and an optional time of day. A window whose end is before
/// its start runs over midnight ("22:00 to 06:00"); then the day is the day it starts on.
/// </summary>
public readonly record struct TimeWindow(int Days, TimeOnly? From, TimeOnly? To)
{
    public const int Weekdays = 0b0111110;

    public const int Weekend = 0b1000001;

    public const int EveryDay = 0;

    public static int Bit(DayOfWeek day) => 1 << (int)day;

    public bool HasDay(DayOfWeek day) => Days == 0 || (Days & Bit(day)) != 0;

    public bool Matches(DateTime local)
    {
        var time = TimeOnly.FromDateTime(local);
        if (From is not { } from || To is not { } to)
        {
            return HasDay(local.DayOfWeek);
        }

        if (from <= to)
        {
            return HasDay(local.DayOfWeek) && time >= from && time < to;
        }

        // Over midnight: the evening part belongs to the day, the morning part to the day before.
        if (time >= from)
        {
            return HasDay(local.DayOfWeek);
        }

        return time < to && HasDay(local.AddDays(-1).DayOfWeek);
    }

    public static int Of(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (bits, d) => bits | Bit(d));

    public static IEnumerable<DayOfWeek> DaysOf(int bits)
        => Enum.GetValues<DayOfWeek>().Where(d => bits != 0 && (bits & Bit(d)) != 0);
}
