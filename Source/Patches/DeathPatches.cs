using System;
using HarmonyLib;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Patches;

/// <summary>
/// 死亡相关 Harmony 补丁
/// 监听梦境模式下的死亡和重生事件
/// </summary>
[HarmonyPatch(typeof(HeroController))]
internal static class HeroControllerDeathPatches
{
    /// <summary>
    /// 监听玩家死亡（仅梦境模式）
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch("Die")]
    private static void OnPlayerDiePostfix(bool nonLethal)
    {
        try
        {
            if (DeathManager.Instance != null && MemoryManager.IsInMemoryMode)
            {
                Log.Info($"[DeathManager] 梦境中死亡，nonLethal={nonLethal}，设置等待重生标志");
                DeathManager.Instance.SetWaitingForMemoryRespawn();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DeathManager] Die 补丁失败: {ex.Message}");
        }
    }
}

/// <summary>
/// 重生信息 Harmony 补丁
/// 在梦境模式下修改重生信息，让玩家在当前场景复活
/// </summary>
[HarmonyPatch(typeof(GameManager))]
internal static class GameManagerRespawnPatches
{
    /// <summary>
    /// 拦截 GetRespawnInfo，在梦境模式下强制返回当前场景
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch("GetRespawnInfo")]
    private static void OnGetRespawnInfoPostfix(ref string scene, ref string marker)
    {
        try
        {
            if (MemoryManager.IsInMemoryMode)
            {
                string originalScene = scene;
                string originalMarker = marker;

                // 强制设置为当前梦境场景和我们创建的重生标记
                scene = "Cradle_03";
                marker = "MemoryRespawnMarker";

                Log.Info($"[DeathManager] 梦境模式修改重生信息: {originalScene}/{originalMarker} → {scene}/{marker}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DeathManager] GetRespawnInfo 补丁失败: {ex.Message}");
        }
    }
}
