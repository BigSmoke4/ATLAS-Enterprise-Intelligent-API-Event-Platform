using Atlas.Modules.AIOperations.Application;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>A fake read tool that returns canned success/failure, standing in for a real ATLAS data source in tests.</summary>
file class FakeTool : IAtlasTool
{
    private readonly bool _success;
    private readonly string _summary;
    public FakeTool(string name, bool success, string summary) { Name = name; _success = success; _summary = summary; }
    public string Name { get; }
    public string Description => "Fake tool for testing.";
    public ToolKind Kind => ToolKind.Read;
    public Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
        => Task.FromResult(new ToolResult(_success, _summary));
}

public class AiOperationsAssistantTests
{
    [Fact]
    public async Task Returns_insufficient_evidence_when_no_tool_has_data()
    {
        var assistant = new AiOperationsAssistant(new[] { new FakeTool("GetServiceHealth", success: false, summary: "") }, new ActionToolGuard());

        var answer = await assistant.AnswerAsync(Guid.NewGuid(), "Is payments healthy?", new[] { "GetServiceHealth" });

        Assert.False(answer.HasSufficientEvidence);
        Assert.Equal("Insufficient evidence.", answer.Text);
        Assert.Empty(answer.Citations);
    }

    [Fact]
    public async Task Returns_grounded_answer_with_citation_when_tool_has_data()
    {
        var assistant = new AiOperationsAssistant(new[] { new FakeTool("GetServiceHealth", success: true, summary: "payments is Healthy") }, new ActionToolGuard());

        var answer = await assistant.AnswerAsync(Guid.NewGuid(), "Is payments healthy?", new[] { "GetServiceHealth" });

        Assert.True(answer.HasSufficientEvidence);
        Assert.Contains("payments is Healthy", answer.Text);
        Assert.Single(answer.Citations);
        Assert.Equal("GetServiceHealth", answer.Citations[0].ToolName);
    }

    [Fact]
    public async Task Unknown_tool_name_is_skipped_not_treated_as_evidence()
    {
        var assistant = new AiOperationsAssistant(new[] { new FakeTool("GetServiceHealth", success: true, summary: "x") }, new ActionToolGuard());

        var answer = await assistant.AnswerAsync(Guid.NewGuid(), "question", new[] { "NotARealTool" });

        Assert.False(answer.HasSufficientEvidence);
    }

    [Fact]
    public async Task Combines_evidence_from_multiple_tools()
    {
        var assistant = new AiOperationsAssistant(new IAtlasTool[]
        {
            new FakeTool("GetServiceHealth", true, "payments is Degraded"),
            new FakeTool("GetIncidentHistory", true, "\"Payment errors\" is Investigating"),
        }, new ActionToolGuard());

        var answer = await assistant.AnswerAsync(Guid.NewGuid(), "What's going on with payments?",
            new[] { "GetServiceHealth", "GetIncidentHistory" });

        Assert.Equal(2, answer.Citations.Count);
    }
}

public class ActionToolGuardTests
{
    private class FakeActionTool : IAtlasTool
    {
        public string Name => "RestartService";
        public string Description => "Destructive action.";
        public ToolKind Kind => ToolKind.Action;
        public Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "restarted"));
    }

    private class FakeReadTool : IAtlasTool
    {
        public string Name => "GetServiceHealth";
        public string Description => "Read-only.";
        public ToolKind Kind => ToolKind.Read;
        public Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "ok"));
    }

    [Fact]
    public void Read_tools_never_require_authorization_or_confirmation()
    {
        var guard = new ActionToolGuard();
        var decision = guard.Authorize(new FakeReadTool(), callerIsAuthorized: false, explicitConfirmationGiven: false);
        Assert.True(decision.Authorized);
    }

    [Fact]
    public void Action_tool_denied_without_authorization()
    {
        var guard = new ActionToolGuard();
        var decision = guard.Authorize(new FakeActionTool(), callerIsAuthorized: false, explicitConfirmationGiven: true);
        Assert.False(decision.Authorized);
    }

    [Fact]
    public void Action_tool_denied_without_explicit_confirmation_even_if_authorized()
    {
        var guard = new ActionToolGuard();
        var decision = guard.Authorize(new FakeActionTool(), callerIsAuthorized: true, explicitConfirmationGiven: false);
        Assert.False(decision.Authorized);
    }

    [Fact]
    public void Action_tool_allowed_when_authorized_and_confirmed()
    {
        var guard = new ActionToolGuard();
        var decision = guard.Authorize(new FakeActionTool(), callerIsAuthorized: true, explicitConfirmationGiven: true);
        Assert.True(decision.Authorized);
    }
}

public class AiOperationsAssistantExecuteActionTests
{
    private class FakeActionTool : IAtlasTool
    {
        public bool WasInvoked { get; private set; }
        public string Name => "DeactivatePolicy";
        public string Description => "test action";
        public ToolKind Kind => ToolKind.Action;
        public Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
        {
            WasInvoked = true;
            return Task.FromResult(new ToolResult(true, "deactivated"));
        }
    }

    [Fact]
    public async Task Denies_execution_without_confirmation_and_never_invokes_the_tool()
    {
        var tool = new FakeActionTool();
        var assistant = new AiOperationsAssistant(new IAtlasTool[] { tool }, new ActionToolGuard());

        var result = await assistant.ExecuteActionAsync(Guid.NewGuid(), "DeactivatePolicy", new Dictionary<string, string>(),
            callerIsAuthorized: true, explicitConfirmationGiven: false);

        Assert.False(result.Success);
        Assert.False(tool.WasInvoked);
    }

    [Fact]
    public async Task Denies_execution_without_authorization_even_if_confirmed()
    {
        var tool = new FakeActionTool();
        var assistant = new AiOperationsAssistant(new IAtlasTool[] { tool }, new ActionToolGuard());

        var result = await assistant.ExecuteActionAsync(Guid.NewGuid(), "DeactivatePolicy", new Dictionary<string, string>(),
            callerIsAuthorized: false, explicitConfirmationGiven: true);

        Assert.False(result.Success);
        Assert.False(tool.WasInvoked);
    }

    [Fact]
    public async Task Executes_when_authorized_and_confirmed()
    {
        var tool = new FakeActionTool();
        var assistant = new AiOperationsAssistant(new IAtlasTool[] { tool }, new ActionToolGuard());

        var result = await assistant.ExecuteActionAsync(Guid.NewGuid(), "DeactivatePolicy", new Dictionary<string, string>(),
            callerIsAuthorized: true, explicitConfirmationGiven: true);

        Assert.True(result.Success);
        Assert.True(tool.WasInvoked);
    }

    [Fact]
    public async Task Unknown_action_tool_name_fails_cleanly()
    {
        var assistant = new AiOperationsAssistant(Array.Empty<IAtlasTool>(), new ActionToolGuard());

        var result = await assistant.ExecuteActionAsync(Guid.NewGuid(), "NoSuchTool", new Dictionary<string, string>(),
            callerIsAuthorized: true, explicitConfirmationGiven: true);

        Assert.False(result.Success);
    }
}
