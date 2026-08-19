namespace UpgradeTool.Core.Abstractions;

public enum FlashStage
{
    Idle,
    OpeningDevice,
    EnteringUpdateMode,
    Downloading,
    Verifying,
    Exporting,
    Completed,
    Failed,
    Cancelled,
}

public sealed record FlashProgress(FlashStage Stage, int Percent, string Message);
