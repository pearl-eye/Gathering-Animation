using System.Text;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using InteropGenerator.Runtime;
using Lumina.Misc;

namespace GatherAnimation;

internal sealed unsafe class GatherAnimationHook : IDisposable
{
    private static readonly byte[] ReplacementPath = Encoding.UTF8.GetBytes(AnimationPaths.Replacement + '\0');
    private static readonly uint ReplacementHash = ~Crc32.Get(AnimationPaths.Replacement);
    private readonly Hook<ResourceManager.Delegates.GetResourceSync> syncHook;
    private readonly Hook<ResourceManager.Delegates.GetResourceAsync> asyncHook;
    private readonly Configuration configuration;
    private readonly ICondition condition;
    private readonly IFramework framework;
    private readonly IObjectTable objects;
    private volatile int activeAnimations;
    private long replacementCount;
    private long miningReplacementCount;
    private long botanyReplacementCount;
    private long fishingReplacementCount;

    public long ReplacementCount => Interlocked.Read(ref replacementCount);
    public bool InterceptEnabled => activeAnimations != 0;
    public bool MiningInterceptEnabled => activeAnimations == 2;
    public bool BotanyInterceptEnabled => activeAnimations == 4;
    public bool FishingInterceptEnabled => activeAnimations == 1;
    public long MiningReplacementCount => Interlocked.Read(ref miningReplacementCount);
    public long BotanyReplacementCount => Interlocked.Read(ref botanyReplacementCount);
    public long FishingReplacementCount => Interlocked.Read(ref fishingReplacementCount);
    public int NearbyPlayerCount { get; private set; }
    public float? NearestPlayerDistance { get; private set; }
    public bool PausedForNearbyPlayers { get; private set; }

    public GatherAnimationHook(IGameInteropProvider interop, ICondition condition, IFramework framework,
        IObjectTable objects, Configuration configuration)
    {
        this.configuration = configuration;
        this.condition = condition;
        this.framework = framework;
        this.objects = objects;
        syncHook = interop.HookFromAddress<ResourceManager.Delegates.GetResourceSync>(
            (nint)ResourceManager.MemberFunctionPointers.GetResourceSync, GetResourceSyncDetour);
        asyncHook = interop.HookFromAddress<ResourceManager.Delegates.GetResourceAsync>(
            (nint)ResourceManager.MemberFunctionPointers.GetResourceAsync, GetResourceAsyncDetour);
        RefreshState();
        syncHook.Enable();
        asyncHook.Enable();
        framework.Update += OnFrameworkUpdate;
        condition.ConditionChange += OnConditionChange;
    }

    public void RefreshState()
    {
        var localPlayer = objects.LocalPlayer;
        var nearbyCount = 0;
        var nearestDistanceSquared = float.PositiveInfinity;
        if (localPlayer != null)
        {
            var position = localPlayer.Position;
            var rangeSquared = configuration.PlayerDetectionRange * configuration.PlayerDetectionRange;
            foreach (var character in objects.PlayerObjects)
            {
                if (character is not IPlayerCharacter || character.Address == localPlayer.Address)
                    continue;
                var distanceSquared = Vector3.DistanceSquared(position, character.Position);
                nearestDistanceSquared = MathF.Min(nearestDistanceSquared, distanceSquared);
                if (distanceSquared <= rangeSquared)
                    nearbyCount++;
            }
        }
        NearbyPlayerCount = nearbyCount;
        NearestPlayerDistance = float.IsFinite(nearestDistanceSquared) ? MathF.Sqrt(nearestDistanceSquared) : null;
        PausedForNearbyPlayers = configuration.Enabled && configuration.DisableWhenPlayersAround && nearbyCount > 0;
        activeAnimations = configuration.Enabled && !PausedForNearbyPlayers ? localPlayer?.ClassJob.RowId switch
        {
            16 when configuration.MiningEnabled && condition[ConditionFlag.Gathering] => 2,
            17 when configuration.BotanyEnabled && condition[ConditionFlag.Gathering] => 4,
            18 when configuration.FishingEnabled && condition[ConditionFlag.Fishing] => 1,
            _ => 0,
        } : 0;
    }

    private void OnFrameworkUpdate(IFramework _) => RefreshState();
    private void OnConditionChange(ConditionFlag flag, bool value) => RefreshState();

    private bool ShouldReplace(CStringPointer path)
    {
        var active = activeAnimations;
        if (active == 0 || !path.HasValue)
            return false;
        var bytes = path.AsSpan();
        var matches = active == 1
            ? (bytes.StartsWith("chara/action/fishing"u8) || bytes.StartsWith("chara/action/event_base/event_base_fishing"u8))
                && AnimationPaths.Targets.Contains(path.ToString())
            : bytes.StartsWith("chara/action/gather/"u8) && AnimationPaths.GatheringTargets.Contains(path.ToString());
        if (!matches)
            return false;

        Interlocked.Increment(ref replacementCount);
        switch (active)
        {
            case 1: Interlocked.Increment(ref fishingReplacementCount); break;
            case 2: Interlocked.Increment(ref miningReplacementCount); break;
            case 4: Interlocked.Increment(ref botanyReplacementCount); break;
        }
        return true;
    }

    private ResourceHandle* GetResourceSyncDetour(ResourceManager* manager, ResourceCategory* category,
        uint* type, uint* hash, CStringPointer path, void* unknown, void* debugPointer, uint debugValue)
    {
        if (!ShouldReplace(path))
            return syncHook.Original(manager, category, type, hash, path, unknown, debugPointer, debugValue);

        var replacementHash = ReplacementHash;
        fixed (byte* replacementPath = ReplacementPath)
            return syncHook.Original(manager, category, type, &replacementHash, replacementPath, unknown, debugPointer, debugValue);
    }

    private ResourceHandle* GetResourceAsyncDetour(ResourceManager* manager, ResourceCategory* category,
        uint* type, uint* hash, CStringPointer path, void* unknown, bool isUnknown, void* debugPointer, uint debugValue)
    {
        if (!ShouldReplace(path))
            return asyncHook.Original(manager, category, type, hash, path, unknown, isUnknown, debugPointer, debugValue);

        var replacementHash = ReplacementHash;
        fixed (byte* replacementPath = ReplacementPath)
            return asyncHook.Original(manager, category, type, &replacementHash, replacementPath, unknown, isUnknown, debugPointer, debugValue);
    }

    public void Dispose()
    {
        activeAnimations = 0;
        framework.Update -= OnFrameworkUpdate;
        condition.ConditionChange -= OnConditionChange;
        asyncHook.Dispose();
        syncHook.Dispose();
    }
}
