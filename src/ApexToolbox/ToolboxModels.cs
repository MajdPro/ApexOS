namespace ApexToolbox;

internal sealed class ToolboxConfiguration
{
    public int Version { get; init; }
    public string Product { get; init; } = "Apex Toolbox";
    public List<ToolboxAction> Actions { get; init; } = [];
}

internal sealed class ToolboxAction
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Script { get; init; } = "";
    public string RiskTier { get; init; } = "";
    public string ApplyText { get; init; } = "Apply";
    public string RestoreText { get; init; } = "Restore";
    public List<string> StatusArgs { get; init; } = [];
    public List<string> ApplyArgs { get; init; } = [];
    public List<string> RestoreArgs { get; init; } = [];
    public bool RequiresAdmin { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool OfferRestorePoint { get; init; }
    public bool ReadOnly { get; init; }
}

internal sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);