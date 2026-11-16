using Dalamud.Configuration;

namespace GatherAnimation;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const float MinimumPlayerDetectionRange = 1f;
    public const float MaximumPlayerDetectionRange = 200f;

    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool MiningEnabled { get; set; } = true;
    public bool BotanyEnabled { get; set; } = true;
    public bool FishingEnabled { get; set; } = true;
    public bool DisableWhenPlayersAround { get; set; } = true;
    public float PlayerDetectionRange { get; set; } = 50f;

    public static void ValidatePlayerDetectionRange(float range)
    {
        if (!float.IsFinite(range) || range < MinimumPlayerDetectionRange || range > MaximumPlayerDetectionRange)
            throw new ArgumentOutOfRangeException(nameof(range), "检测距离需要在 1 至 200 米之间。");
    }
}
