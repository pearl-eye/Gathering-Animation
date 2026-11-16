using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace GatherAnimation;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly Configuration configuration;
    private readonly GatherAnimationHook animationHook;
    private readonly WindowSystem windows = new("Gather animation");
    private readonly SettingsWindow settings;

    public static Plugin? Instance { get; private set; }
    public bool Enabled => configuration.Enabled;
    public bool InterceptEnabled => animationHook.InterceptEnabled;
    public long ReplacementCount => animationHook.ReplacementCount;
    public bool MiningEnabled => configuration.MiningEnabled;
    public bool BotanyEnabled => configuration.BotanyEnabled;
    public bool FishingEnabled => configuration.FishingEnabled;
    public bool MiningInterceptEnabled => animationHook.MiningInterceptEnabled;
    public bool BotanyInterceptEnabled => animationHook.BotanyInterceptEnabled;
    public bool FishingInterceptEnabled => animationHook.FishingInterceptEnabled;
    public long MiningReplacementCount => animationHook.MiningReplacementCount;
    public long BotanyReplacementCount => animationHook.BotanyReplacementCount;
    public long FishingReplacementCount => animationHook.FishingReplacementCount;
    public bool DisableWhenPlayersAround => configuration.DisableWhenPlayersAround;
    public float PlayerDetectionRange => configuration.PlayerDetectionRange;
    public int NearbyPlayerCount => animationHook.NearbyPlayerCount;
    public float? NearestPlayerDistance => animationHook.NearestPlayerDistance;
    public bool PausedForNearbyPlayers => animationHook.PausedForNearbyPlayers;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IChatGui chat,
        IGameInteropProvider interop, ICondition condition, IFramework framework, IDataManager data, IObjectTable objects)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.chat = chat;
        foreach (var path in AnimationPaths.Targets.Concat(AnimationPaths.GatheringTargets))
            if (!data.FileExists(path))
                throw new FileNotFoundException("游戏中缺少采集动画资源。", path);

        configuration = (Configuration?)pluginInterface.GetPluginConfig() ?? new Configuration();
        Configuration.ValidatePlayerDetectionRange(configuration.PlayerDetectionRange);
        animationHook = new GatherAnimationHook(interop, condition, framework, objects, configuration);
        settings = new SettingsWindow(this);
        windows.AddWindow(settings);
        var commandInfo = new CommandInfo(OnCommand)
        {
            HelpMessage = "打开采集动画设置；/gatheranim on 启用；/gatheranim off 关闭；/gatheranim status 查看状态。",
        };
        commands.AddHandler("/gatheranim", commandInfo);
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        pluginInterface.UiBuilder.OpenMainUi += OpenSettings;
        Instance = this;
        pluginInterface.SavePluginConfig(configuration);
    }

    public void SetEnabled(bool enabled)
    {
        configuration.Enabled = enabled;
        animationHook.RefreshState();
        pluginInterface.SavePluginConfig(configuration);
    }

    public void SetJobEnabled(uint job, bool enabled)
    {
        switch (job)
        {
            case 16: configuration.MiningEnabled = enabled; break;
            case 17: configuration.BotanyEnabled = enabled; break;
            case 18: configuration.FishingEnabled = enabled; break;
            default: throw new ArgumentOutOfRangeException(nameof(job));
        }
        animationHook.RefreshState();
        pluginInterface.SavePluginConfig(configuration);
    }

    public void SetPlayerDetection(bool enabled, float range)
    {
        Configuration.ValidatePlayerDetectionRange(range);
        configuration.DisableWhenPlayersAround = enabled;
        configuration.PlayerDetectionRange = range;
        animationHook.RefreshState();
        pluginInterface.SavePluginConfig(configuration);
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "":
                OpenSettings();
                break;
            case "on":
                SetEnabled(true);
                PrintStatus();
                break;
            case "off":
                SetEnabled(false);
                PrintStatus();
                break;
            case "status":
                PrintStatus();
                break;
            default:
                chat.Print("[Gathering Animation] 使用 /gatheranim、/gatheranim on、/gatheranim off 或 /gatheranim status。");
                break;
        }
    }

    private string ActivityStatus => !Enabled ? "已关闭" : PausedForNearbyPlayers ? "附近存在其他玩家，已暂停" : InterceptEnabled ? "正在替换采集动画" : "当前未替换采集动画";
    private void PrintStatus() => chat.Print($"[Gathering Animation] 总开关：{State(Enabled)}；采矿工：{State(MiningEnabled)}；园艺工：{State(BotanyEnabled)}；捕鱼人：{State(FishingEnabled)}。附近玩家检测：{State(DisableWhenPlayersAround)}，范围 {PlayerDetectionRange:F1} 米，范围内 {NearbyPlayerCount} 人。状态：{ActivityStatus}。");
    private static string State(bool enabled) => enabled ? "已启用" : "已关闭";
    private void OpenSettings() => settings.IsOpen = true;

    public void Dispose()
    {
        Instance = null;
        commands.RemoveHandler("/gatheranim");
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        pluginInterface.UiBuilder.OpenMainUi -= OpenSettings;
        windows.RemoveAllWindows();
        animationHook.Dispose();
    }

    private sealed class SettingsWindow(Plugin plugin) : Window("Gathering Animation###GatherAnimationSettings")
    {
        public override void Draw()
        {
            var enabled = plugin.Enabled;
            if (ImGui.Checkbox("启用采集无后摇", ref enabled))
                plugin.SetEnabled(enabled);
            var mining = plugin.MiningEnabled;
            if (ImGui.Checkbox("采矿工", ref mining))
                plugin.SetJobEnabled(16, mining);
            var botany = plugin.BotanyEnabled;
            if (ImGui.Checkbox("园艺工", ref botany))
                plugin.SetJobEnabled(17, botany);
            var fishing = plugin.FishingEnabled;
            if (ImGui.Checkbox("捕鱼人", ref fishing))
                plugin.SetJobEnabled(18, fishing);
            ImGui.Separator();
            var detectPlayers = plugin.DisableWhenPlayersAround;
            if (ImGui.Checkbox("附近有其他玩家时自动暂停", ref detectPlayers))
                plugin.SetPlayerDetection(detectPlayers, plugin.PlayerDetectionRange);
            var detectionRange = plugin.PlayerDetectionRange;
            if (ImGui.SliderFloat("检测距离（米）", ref detectionRange,
                Configuration.MinimumPlayerDetectionRange, Configuration.MaximumPlayerDetectionRange,
                "%.1f", ImGuiSliderFlags.AlwaysClamp))
                plugin.SetPlayerDetection(plugin.DisableWhenPlayersAround, detectionRange);
            ImGui.TextUnformatted($"范围内其他玩家：{plugin.NearbyPlayerCount} 人");
            if (plugin.NearestPlayerDistance is { } nearest)
                ImGui.TextUnformatted($"最近的其他玩家：{nearest:F1} 米");
            ImGui.TextUnformatted($"状态：{plugin.ActivityStatus}");
        }
    }
}
