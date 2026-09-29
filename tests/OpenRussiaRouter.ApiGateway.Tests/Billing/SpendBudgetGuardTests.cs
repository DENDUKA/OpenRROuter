using System.Text.Json.Nodes;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.Billing;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

public sealed class SpendBudgetGuardTests
{
    // $1 per 1M input tokens, $10 per 1M output tokens.
    private const decimal InputPrice = 1m;
    private const decimal OutputPrice = 10m;

    [Fact]
    public void Apply_HealthyBalance_LeavesBodyUntouched()
    {
        var node = JsonNode.Parse("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""")!;
        var before = node.ToJsonString();

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 10m, InputPrice, OutputPrice);

        decision.Should().Be(SpendBudgetDecision.Unchanged);
        node.ToJsonString().Should().Be(before);
    }

    [Fact]
    public void Apply_LowBalanceWithoutLimit_InjectsAffordableMaxTokens()
    {
        var node = JsonNode.Parse("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""")!;

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 0.01m, InputPrice, OutputPrice);

        decision.Rejected.Should().BeFalse();
        decision.OutputLimit.Should().BeInRange(SpendBudgetGuard.MinOutputTokens, 1_000);
        node["max_tokens"]!.GetValue<int>().Should().Be(decision.OutputLimit);
    }

    [Fact]
    public void Apply_ClientLimitAboveBudget_LowersClientField()
    {
        var node = JsonNode.Parse("""{"model":"m","max_completion_tokens":50000,"messages":[{"role":"user","content":"hi"}]}""")!;

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 0.1m, InputPrice, OutputPrice);

        decision.OutputLimit.Should().BeInRange(9_000, 10_000);
        node["max_completion_tokens"]!.GetValue<int>().Should().Be(decision.OutputLimit);
        node["max_tokens"].Should().BeNull();
    }

    [Fact]
    public void Apply_ClientLimitWithinBudget_LeavesBodyUntouched()
    {
        var node = JsonNode.Parse("""{"model":"m","max_tokens":100,"messages":[{"role":"user","content":"hi"}]}""")!;

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 0.1m, InputPrice, OutputPrice);

        decision.Should().Be(SpendBudgetDecision.Unchanged);
        node["max_tokens"]!.GetValue<int>().Should().Be(100);
    }

    [Fact]
    public void Apply_InputCostExceedsBalance_Rejects()
    {
        // ~1M estimated input tokens = ~$1 against a $0.50 balance.
        var node = new JsonObject
        {
            ["model"] = "m",
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new string('a', 3_000_000) })
        };

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 0.5m, InputPrice, OutputPrice);

        decision.Rejected.Should().BeTrue();
    }

    [Fact]
    public void Apply_BudgetBelowMinimumOutput_Rejects()
    {
        var node = JsonNode.Parse("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""")!;

        // $0.001 buys ~100 output tokens — too little to be worth sending.
        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 0.001m, InputPrice, OutputPrice);

        decision.Rejected.Should().BeTrue();
    }

    [Fact]
    public void Apply_Base64Images_AreNotCountedAsInput()
    {
        var dataUrl = "data:image/png;base64," + new string('A', 3_000_000);
        var node = new JsonObject
        {
            ["model"] = "m",
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "text", ["text"] = "what is this?" },
                    new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = dataUrl } })
            })
        };

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 0.5m, InputPrice, OutputPrice);

        decision.Rejected.Should().BeFalse();
        decision.EstimatedInputTokens.Should().BeLessThan(1_000);
    }

    [Fact]
    public void Apply_FreeModel_LeavesBodyUntouched()
    {
        var node = JsonNode.Parse("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""")!;

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: false, 0m, 0m, 0m);

        decision.Should().Be(SpendBudgetDecision.Unchanged);
    }

    [Fact]
    public void Apply_Anthropic_LowersMaxTokensAndThinkingBudget()
    {
        var node = JsonNode.Parse("""
            {"model":"m","max_tokens":32000,"thinking":{"type":"enabled","budget_tokens":16000},
             "messages":[{"role":"user","content":[{"type":"image","source":{"type":"base64","media_type":"image/png","data":"AAAA"}},{"type":"text","text":"hi"}]}]}
            """)!;

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: true, 0.02m, InputPrice, OutputPrice);

        decision.OutputLimit.Should().BeInRange(1_900, 2_000);
        node["max_tokens"]!.GetValue<int>().Should().Be(decision.OutputLimit);
        node["thinking"]!["budget_tokens"]!.GetValue<int>().Should().Be(decision.OutputLimit!.Value - 1);
    }

    [Fact]
    public void Apply_Anthropic_BudgetBelowThinkingMinimum_DropsThinking()
    {
        var node = JsonNode.Parse("""
            {"model":"m","max_tokens":32000,"thinking":{"type":"enabled","budget_tokens":16000},
             "messages":[{"role":"user","content":"hi"}]}
            """)!;

        var decision = SpendBudgetGuard.Apply(node, isAnthropic: true, 0.008m, InputPrice, OutputPrice);

        decision.OutputLimit.Should().BeInRange(SpendBudgetGuard.MinOutputTokens, 1_024);
        node["thinking"].Should().BeNull();
    }
}
