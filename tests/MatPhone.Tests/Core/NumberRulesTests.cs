using MatPhone.Core.Telephony;

namespace MatPhone.Tests.Core;

/// <summary>Numbers as people type them and providers send them become one form inside the installation, and the form a provider wants on the way out.</summary>
public sealed class NumberRulesTests
{
    private static readonly NumberPlan Berlin = new("49", "030", "0");

    [Theory]
    [InlineData("+49 (0)30 123 456-7", "+49301234567")]
    [InlineData(" 030 / 12 34 56 ", "030123456")]
    [InlineData("*86#", "*86#")]
    [InlineData("49+30", "4930")]
    [InlineData(null, "")]
    public void Clean_keeps_only_what_can_be_dialled(string? input, string expected) => Assert.Equal(expected, NumberRules.Clean(input));

    [Theory]
    [InlineData("+4930123456", "+4930123456")]
    [InlineData("004930123456", "+4930123456")]
    [InlineData("030 123456", "+4930123456")]
    [InlineData("0151 2345678", "+491512345678")]
    [InlineData("123456", "+4930123456")]
    [InlineData("0043 1 234567", "+431234567")]
    [InlineData("201", null)]
    [InlineData("*86", null)]
    [InlineData("", null)]
    public void ToE164_writes_every_external_number_with_the_country(string input, string? expected) => Assert.Equal(expected, NumberRules.ToE164(input, Berlin));

    [Fact]
    public void A_local_number_needs_an_area_code()
        => Assert.Null(NumberRules.ToE164("123456", new NumberPlan("49", "", "0")));

    [Theory]
    [InlineData("201", true)]
    [InlineData("12345678", true)]
    [InlineData("123456789", false)]
    [InlineData("+49301", false)]
    [InlineData("*86", false)]
    public void An_extension_is_short_and_only_digits(string number, bool expected) => Assert.Equal(expected, NumberRules.IsExtension(number));

    [Fact]
    public void The_outside_line_prefix_is_taken_off_once()
    {
        Assert.Equal("030123456", NumberRules.StripOutsideLine("0030123456", Berlin));
        Assert.Null(NumberRules.StripOutsideLine("201", Berlin));
        Assert.Null(NumberRules.StripOutsideLine("0", Berlin));
        Assert.Equal("030123456", NumberRules.StripOutsideLine("030123456", new NumberPlan("49", "030", "")));
    }

    [Theory]
    [InlineData(NumberFormatKind.E164Plus, "+4930123456")]
    [InlineData(NumberFormatKind.E164, "4930123456")]
    [InlineData(NumberFormatKind.National, "030123456")]
    public void Format_writes_what_the_provider_wants(NumberFormatKind format, string expected)
        => Assert.Equal(expected, NumberRules.Format("+4930123456", format, Berlin));

    [Fact]
    public void A_foreign_number_in_national_format_keeps_its_country()
        => Assert.Equal("00431234567", NumberRules.Format("+431234567", NumberFormatKind.National, Berlin));

    [Theory]
    [InlineData("+4930123456", "030 123456")]
    [InlineData("+491512345678", "01512345678")]
    [InlineData("+431234567", "+431234567")]
    [InlineData("201", "201")]
    public void Display_writes_the_own_country_nationally(string number, string expected) => Assert.Equal(expected, NumberRules.Display(number, Berlin));

    [Fact]
    public void Two_ways_of_writing_are_the_same_caller()
    {
        Assert.True(NumberRules.SameNumber("+4930123456", "030 123456", Berlin));
        Assert.True(NumberRules.SameNumber("201", "201", Berlin));
        Assert.False(NumberRules.SameNumber("+4930123456", "+4930123457", Berlin));
        Assert.False(NumberRules.SameNumber(null, "201", Berlin));
    }
}

/// <summary>The patterns of the rules.</summary>
public sealed class DialPatternTests
{
    [Theory]
    [InlineData("0900.", "0900123", true)]
    [InlineData("0900.", "0900", false)]
    [InlineData("0900!", "0900", true)]
    [InlineData("11X", "110", true)]
    [InlineData("11X", "1100", false)]
    [InlineData("0[1-9].", "0151", true)]
    [InlineData("0[1-9].", "0049", false)]
    [InlineData("[137]00", "300", true)]
    [InlineData("[137]00", "200", false)]
    [InlineData("Z00", "000", false)]
    [InlineData("N00", "100", false)]
    [InlineData("n00", "200", true)]
    [InlineData("+49151.", "+491511234", true)]
    [InlineData("", "anything", true)]
    [InlineData("201", "", false)]
    public void Matches_follows_the_rules_of_the_pattern(string pattern, string number, bool expected) => Assert.Equal(expected, DialPattern.Matches(pattern, number));

    [Theory]
    [InlineData("0900.", true)]
    [InlineData("0[1-9]X!", true)]
    [InlineData("", true)]
    [InlineData("09.00", false)]
    [InlineData("0[1-9", false)]
    [InlineData("0[]", false)]
    [InlineData("0[a]", false)]
    [InlineData("abc", false)]
    public void IsValid_refuses_what_Matches_does_not_know(string pattern, bool expected) => Assert.Equal(expected, DialPattern.IsValid(pattern));
}

/// <summary>When a rule applies.</summary>
public sealed class TimeWindowTests
{
    // 2026-10-23 is a Friday
    private static DateTime Friday(int hour, int minute = 0) => new(2026, 10, 23, hour, minute, 0);

    [Fact]
    public void Office_hours_on_weekdays()
    {
        var office = new TimeWindow(TimeWindow.Weekdays, new TimeOnly(8, 0), new TimeOnly(17, 0));
        Assert.True(office.Matches(Friday(8)));
        Assert.True(office.Matches(Friday(16, 59)));
        Assert.False(office.Matches(Friday(17)));
        Assert.False(office.Matches(Friday(7, 59)));
        Assert.False(office.Matches(Friday(10).AddDays(1)), "Saturday");
    }

    [Fact]
    public void A_window_over_midnight_belongs_to_the_day_it_starts_on()
    {
        var night = new TimeWindow(TimeWindow.Bit(DayOfWeek.Friday), new TimeOnly(22, 0), new TimeOnly(6, 0));
        Assert.True(night.Matches(Friday(23)));
        Assert.True(night.Matches(Friday(5).AddDays(1)), "Saturday morning belongs to Friday night");
        Assert.False(night.Matches(Friday(5)), "Friday morning belongs to Thursday night");
        Assert.False(night.Matches(Friday(12)));
    }

    [Fact]
    public void Without_days_and_times_it_is_always()
    {
        var always = new TimeWindow(TimeWindow.EveryDay, null, null);
        Assert.True(always.Matches(Friday(3)));
        Assert.True(always.Matches(Friday(3).AddDays(2)));
    }

    [Fact]
    public void The_days_go_to_bits_and_back()
    {
        var bits = TimeWindow.Of([DayOfWeek.Saturday, DayOfWeek.Sunday]);
        Assert.Equal(TimeWindow.Weekend, bits);
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Saturday], TimeWindow.DaysOf(bits));
        Assert.Empty(TimeWindow.DaysOf(0));
    }
}

/// <summary>Which rule decides a call.</summary>
public sealed class RuleMatcherTests
{
    private static readonly NumberPlan Plan = new("49", "030", "0");

    private static readonly TimeWindow Always = new(TimeWindow.EveryDay, null, null);

    private static readonly DateTime Noon = new(2026, 10, 23, 12, 0, 0);

    [Fact]
    public void Inbound_the_first_rule_by_position_wins()
    {
        RuleCandidate[] rules =
        [
            new(1, 20, true, Always, null, null, null),
            new(2, 10, true, Always, "0151.", null, null),
            new(3, 5, false, Always, null, null, null),
        ];

        Assert.Equal(2, RuleMatcher.FirstInbound(rules, Noon, "+491511234567", 7, Plan)!.Id);
        Assert.Equal(1, RuleMatcher.FirstInbound(rules, Noon, "+4930999999", 7, Plan)!.Id);
    }

    [Fact]
    public void Inbound_a_rule_for_one_number_only_sees_calls_to_that_number()
    {
        RuleCandidate[] rules = [new(1, 1, true, Always, null, 5, null)];
        Assert.NotNull(RuleMatcher.FirstInbound(rules, Noon, "+4930999999", 5, Plan));
        Assert.Null(RuleMatcher.FirstInbound(rules, Noon, "+4930999999", 6, Plan));
    }

    [Fact]
    public void Inbound_the_pattern_matches_the_international_form_too()
    {
        RuleCandidate[] rules = [new(1, 1, true, Always, "+43.", null, null)];
        Assert.NotNull(RuleMatcher.FirstInbound(rules, Noon, "+431234567", null, Plan));
        Assert.Null(RuleMatcher.FirstInbound(rules, Noon, null, null, Plan));
    }

    [Fact]
    public void Outbound_the_caller_and_the_dialled_number_decide()
    {
        RuleCandidate[] rules =
        [
            new(1, 1, true, Always, "0900.", null, null),
            new(2, 2, true, Always, null, null, 42),
            new(3, 3, true, new TimeWindow(TimeWindow.Weekend, null, null), null, null, null),
        ];

        Assert.Equal(1, RuleMatcher.FirstOutbound(rules, Noon, "0900123", 42)!.Id);
        Assert.Equal(2, RuleMatcher.FirstOutbound(rules, Noon, "030123", 42)!.Id);
        Assert.Null(RuleMatcher.FirstOutbound(rules, Noon, "030123", 41));
        Assert.Equal(3, RuleMatcher.FirstOutbound(rules, Noon.AddDays(1), "030123", 41)!.Id);
    }

    [Theory]
    [InlineData("0301234", 0, null, "0301234")]
    [InlineData("0301234", 1, "+4930", "+4930301234")]
    [InlineData("123", 5, "9", "9")]
    public void Rewrite_strips_then_prefixes(string number, int strip, string? prefix, string expected) => Assert.Equal(expected, RuleMatcher.Rewrite(number, strip, prefix));
}
