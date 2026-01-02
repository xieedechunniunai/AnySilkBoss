using HarmonyLib;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Patches;

/// <summary>
/// 护符/道具相关 Harmony 补丁
/// 监听护符装备状态变化，更新全局缓存
/// </summary>
[HarmonyPatch(typeof(ToolItemManager))]
internal static class ToolItemPatches
{
    /// <summary>
    /// 后置补丁：在 SetEquippedCrest 方法执行后更新 Reaper 护符状态
    /// </summary>
    [HarmonyPatch("SetEquippedCrest")]
    [HarmonyPostfix]
    private static void SetEquippedCrest_Postfix(string crestId)
    {
        Log.Info($"[ToolItemPatches] 护符装备状态已改变: {crestId}");
        SilkBallManager.UpdateReaperCrestState();
    }

    /// <summary>
    /// 后置补丁：在 RefreshEquippedState 方法执行后更新 Reaper 护符状态
    /// </summary>
    [HarmonyPatch("RefreshEquippedState")]
    [HarmonyPostfix]
    private static void RefreshEquippedState_Postfix()
    {
        Log.Info("[ToolItemPatches] 护符装备状态已刷新");
        SilkBallManager.UpdateReaperCrestState();
    }

    /// <summary>
    /// 后置补丁：在 SendEquippedChangedEvent 方法执行后更新 Reaper 护符状态
    /// </summary>
    [HarmonyPatch("SendEquippedChangedEvent")]
    [HarmonyPostfix]
    private static void SendEquippedChangedEvent_Postfix(bool force)
    {
        Log.Info("[ToolItemPatches] 护符装备变更事件已发送");
        SilkBallManager.UpdateReaperCrestState();
    }
}
