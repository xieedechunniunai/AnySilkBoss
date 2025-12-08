using System;
using System.Collections;
using UnityEngine;
using HarmonyLib;
using AnySilkBoss.Source.Tools;
using TeamCherry.SharedUtils;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 死亡管理器（简化版）
    /// 
    /// 简化后不再拦截梦境死亡，玩家在梦境中死亡会正常复活
    /// 只有退出梦境场景（场景切换/弹琴）才会返回原场景
    /// </summary>
    internal class DeathManager : MonoBehaviour
    {
        #region Singleton
        public static DeathManager Instance { get; private set; }

        /// <summary>
        /// 当玩家完全重生后触发
        /// </summary>
        public event Action OnPlayerFullyRespawned;

        /// <summary>
        /// 当玩家死亡时触发
        /// </summary>
        public event Action OnPlayerDied;

        /// <summary>
        /// 当危险重生开始时触发
        /// </summary>
        public event Action OnHazardRespawnStart;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Log.Info("[DeathManager] 初始化完成（简化版）");
        }

        private void OnEnable()
        {
            StartCoroutine(DelayedPatchApplication());
        }

        private void OnDisable()
        {
            try
            {
                var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID + ".DeathManager");
                harmony.UnpatchSelf();
                Log.Info("[DeathManager] Harmony补丁已卸载");
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathManager] 卸载补丁失败: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private IEnumerator DelayedPatchApplication()
        {
            yield return new WaitForSeconds(2f);
            
            try
            {
                var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID + ".DeathManager");
                harmony.PatchAll(typeof(HeroControllerDeathPatches));
                harmony.PatchAll(typeof(GameManagerRespawnPatches));
                Log.Info("[DeathManager] Harmony补丁已应用");
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathManager] 应用补丁失败: {ex.Message}");
            }
        }
        #endregion

        #region Public Methods
        internal void TriggerPlayerDied()
        {
            Log.Info("[DeathManager] 检测到玩家死亡");
            OnPlayerDied?.Invoke();
        }

        internal void TriggerHazardRespawnStart()
        {
            Log.Info("[DeathManager] 检测到危险重生开始");
            OnHazardRespawnStart?.Invoke();
            StartCoroutine(WaitForFullRespawn());
        }

        public void ManualTriggerRespawn()
        {
            Log.Info("[DeathManager] 手动触发重生事件");
            OnPlayerFullyRespawned?.Invoke();
        }

        public bool IsInMemoryMode()
        {
            return MemoryManager.IsInMemoryMode;
        }
        #endregion

        #region Private Methods
        private IEnumerator WaitForFullRespawn()
        {
            yield return new WaitUntil(() => !IsPlayerDead());
            yield return new WaitForSeconds(0.5f);
            
            if (IsPlayerOnGround() && !IsPlayerDead())
            {
                Log.Info("[DeathManager] 玩家完全重生");
                OnPlayerFullyRespawned?.Invoke();
            }
        }

        private bool IsPlayerDead()
        {
            try
            {
                if (HeroController.instance == null) return false;
                return HeroController.instance.cState.dead;
            }
            catch
            {
                return false;
            }
        }

        private bool IsPlayerOnGround()
        {
            try
            {
                if (HeroController.instance == null) return false;
                return HeroController.instance.cState.onGround;
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }

    /// <summary>
    /// Harmony补丁：监听死亡和重生（不拦截）
    /// </summary>
    [HarmonyPatch(typeof(HeroController))]
    internal static class HeroControllerDeathPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("HazardRespawn")]
        private static void OnHazardRespawnStart(HeroController __instance)
        {
            try
            {
                DeathManager.Instance?.TriggerHazardRespawnStart();
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathManager] HazardRespawn 补丁失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 监听玩家死亡（不拦截，只通知）
        /// 简化后：梦境中死亡正常复活，不返回原场景
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("Die")]
        private static void OnPlayerDiePostfix(HeroController __instance, bool nonLethal)
        {
            try
            {
                if (DeathManager.Instance != null)
                {
                    if (MemoryManager.IsInMemoryMode)
                    {
                        // 简化：梦境中死亡只记录日志，让游戏正常处理复活
                        Log.Info($"[DeathManager] 梦境中死亡，nonLethal={nonLethal}，正常复活");
                    }
                    else
                    {
                        DeathManager.Instance.TriggerPlayerDied();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathManager] Die 补丁失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony补丁：在梦境模式下修改重生信息，让玩家在当前场景复活
    /// 不拦截 PlayerDead，让死亡流程正常执行（尸体、音效、血量恢复等）
    /// 只修改 GetRespawnInfo 返回的重生场景和标记
    /// </summary>
    [HarmonyPatch(typeof(GameManager))]
    internal static class GameManagerRespawnPatches
    {
        /// <summary>
        /// 拦截 GetRespawnInfo，在梦境模式下强制返回当前场景
        /// 这样游戏会在当前场景重生，而不是跳转到 Tut_01
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("GetRespawnInfo")]
        private static void OnGetRespawnInfoPostfix(GameManager __instance, ref string scene, ref string marker)
        {
            try
            {
                if (MemoryManager.IsInMemoryMode)
                {
                    string originalScene = scene;
                    string originalMarker = marker;
                    
                    // 强制设置为当前梦境场景和我们创建的重生标记
                    scene = "Cradle_03";  // TARGET_SCENE
                    marker = "MemoryRespawnMarker";  // MEMORY_RESPAWN_MARKER_NAME
                    
                    Log.Info($"[DeathManager] 梦境模式修改重生信息: {originalScene}/{originalMarker} → {scene}/{marker}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathManager] GetRespawnInfo 补丁失败: {ex.Message}");
            }
        }
    }
}
