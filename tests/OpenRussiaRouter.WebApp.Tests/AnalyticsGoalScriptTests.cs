using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using OpenRussiaRouter.WebApp.Services.Analytics;

namespace OpenRussiaRouter.WebApp.Tests;

public class AnalyticsGoalScriptTests
{
    [Fact]
    public void BuildPayloadJson_NoCountersConfigured_ReturnsNull()
    {
        var json = AnalyticsGoalScript.BuildPayloadJson(
            [AnalyticsGoal.Named(AnalyticsGoals.Signup)], googleTagId: null, yandexCounterId: "", userPseudonym: null);

        json.Should().BeNull("with no counter there is nothing to run and nothing to emit");
    }

    [Fact]
    public void BuildPayloadJson_MetrikaOnly_OmitsGoogleTag()
    {
        var json = AnalyticsGoalScript.BuildPayloadJson(
            goals: null, googleTagId: null, yandexCounterId: "111420552", userPseudonym: null);

        json.Should().NotBeNull();
        json.Should().Contain("\"ym\":\"111420552\"");
        json.Should().NotContain("\"ga\"");
    }

    [Fact]
    public void BuildPayloadJson_GoogleOnly_OmitsMetrikaCounter()
    {
        var json = AnalyticsGoalScript.BuildPayloadJson(
            goals: null, googleTagId: "G-TEST123", yandexCounterId: null, userPseudonym: null);

        json.Should().NotBeNull();
        json.Should().Contain("\"ga\":\"G-TEST123\"");
        json.Should().NotContain("\"ym\"");
    }

    [Fact]
    public void BuildPayloadJson_NoGoals_StillEmitsPayloadForTheClickListener()
    {
        var json = AnalyticsGoalScript.BuildPayloadJson(
            goals: [], googleTagId: null, yandexCounterId: "1", userPseudonym: null);

        json.Should().NotBeNull("the same runner installs the [data-goal] listener");
        json.Should().Contain("\"goals\":[]");
    }

    [Fact]
    public void BuildPayloadJson_ValuedGoal_WritesAmountWithADotUnderRussianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Under ru-RU a hand-built string would produce "1500,50" and break the script —
            // the same trap that already forced the top-up amount to bind as a string.
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            var json = AnalyticsGoalScript.BuildPayloadJson(
                [AnalyticsGoal.Valued(AnalyticsGoals.TopUpCompleted, 1500.50m, "RUB")],
                googleTagId: null, yandexCounterId: "1", userPseudonym: null);

            json.Should().Contain("\"v\":1500.50");
            json.Should().Contain("\"c\":\"RUB\"");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void BuildPayloadJson_PlainGoal_OmitsValueAndCurrency()
    {
        var json = AnalyticsGoalScript.BuildPayloadJson(
            [AnalyticsGoal.Named(AnalyticsGoals.Signup)],
            googleTagId: null, yandexCounterId: "1", userPseudonym: null);

        json.Should().Contain("\"n\":\"signup\"");
        json.Should().NotContain("\"v\"");
        json.Should().NotContain("\"c\"");
    }

    [Fact]
    public void BuildPayloadJson_MultipleGoals_AreAllPreserved()
    {
        var json = AnalyticsGoalScript.BuildPayloadJson(
            [AnalyticsGoal.Named(AnalyticsGoals.Signup), AnalyticsGoal.Named(AnalyticsGoals.EmailVerified)],
            googleTagId: null, yandexCounterId: "1", userPseudonym: null);

        json.Should().Contain("\"n\":\"signup\"");
        json.Should().Contain("\"n\":\"email_verified\"");
    }

    [Fact]
    public void BuildPayloadJson_HostileValue_CannotCloseTheScriptTag()
    {
        // Names come from constants, so this is defence in depth for the JSON island itself.
        var json = AnalyticsGoalScript.BuildPayloadJson(
            [AnalyticsGoal.Named("</script><img src=x onerror=alert(1)>")],
            googleTagId: null, yandexCounterId: "1", userPseudonym: null);

        json.Should().NotContain("</script>");
        json.Should().NotContain("<img");
        json.Should().Contain("\\u003C");
    }

    [Fact]
    public void BuildPayloadJson_UserPseudonym_IsCarriedAndOmittedWhenAbsent()
    {
        AnalyticsGoalScript.BuildPayloadJson(null, null, "1", "abc123")
            .Should().Contain("\"uid\":\"abc123\"");

        AnalyticsGoalScript.BuildPayloadJson(null, null, "1", "  ")
            .Should().NotContain("\"uid\"");
    }

    [Fact]
    public void BuildPayloadJson_ClientTopUpDisabled_CarriesYandexOnlySwitch()
    {
        var json = AnalyticsGoalScript.BuildPayloadJson(
            [AnalyticsGoal.Valued(AnalyticsGoals.TopUpCompleted, 100m, "RUB")],
            googleTagId: "G-TEST",
            yandexCounterId: "1",
            userPseudonym: null,
            yandexTopUpCompletedClientEnabled: false);

        json.Should().Contain("\"ymTopUp\":false");
        json.Should().Contain("\"ga\":\"G-TEST\"");
        json.Should().Contain("\"n\":\"topup_completed\"");
    }
}

public class AnalyticsGoalsTests
{
    private static IReadOnlyList<string> DeclaredConstants() =>
        typeof(AnalyticsGoals)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    [Fact]
    public void All_ContainsEveryDeclaredConstant()
    {
        // A constant missing from All is a goal nobody will remember to register in the cabinets.
        AnalyticsGoals.All.Should().BeEquivalentTo(DeclaredConstants());
    }

    [Fact]
    public void All_HasNoDuplicates()
    {
        AnalyticsGoals.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_UseLowerSnakeCase()
    {
        // Both cabinets match the goal id verbatim against a value typed in by hand; a stray
        // capital letter produces a goal that silently never fires.
        foreach (var goal in AnalyticsGoals.All)
            Regex.IsMatch(goal, "^[a-z][a-z0-9_]*$").Should().BeTrue($"'{goal}' must be lower snake_case");
    }

    [Fact]
    public void Macro_IsTheConversionSubsetOfAll()
    {
        AnalyticsGoals.Macro.Should().BeSubsetOf(AnalyticsGoals.All);
        AnalyticsGoals.Macro.Should().HaveCount(4);
    }

    [Fact]
    public void IsKnown_RejectsUnregisteredNames()
    {
        AnalyticsGoals.IsKnown(AnalyticsGoals.TopUpCompleted).Should().BeTrue();
        AnalyticsGoals.IsKnown("topup_complete").Should().BeFalse();
    }
}
