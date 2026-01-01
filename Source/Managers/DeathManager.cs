using System;
using System.Collections;
using UnityEngine;
using HarmonyLib;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 死亡管理器
    /// 
    /// 功能：
    /// 1. 监听玩家死亡和重生事件
    /// 2. 在重生后自动恢复工具和贝壳碎片
    /// 3. 支持普通重生、危险重生、梦境重生
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
            
            // 订阅自身的重生事件，用于恢复工具
            OnPlayerFullyRespawned += RestorePlayerTools;
            
            Log.Info("[DeathManager] 初始化完成");
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
            // 取消订阅
            OnPlayerFullyRespawned -= RestorePlayerTools;
            
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

        #region Tool Restore
        /// <summary>
        /// 恢复玩家工具和贝壳碎片
        /// </summary>
        private void RestorePlayerTools()
        {
            try
            {
                Log.Info("[DeathManager] 开始恢复道具...");
                
                // 恢复工具
                ToolItemManager.TryReplenishTools(true, ToolItemManager.ReplenishMethod.QuickCraft);
                
                // 恢复贝壳碎片
                GameManager.instance.playerData.ShellShards = 700;

                Log.Info("[DeathManager] 道具恢复完成");
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathManager] 恢复道具失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 公开方法：手动触发道具恢复
        /// </summary>
        public void ManualRestoreTools()
        {
            Log.Info("[DeathManager] 手动触发道具恢复");
            RestorePlayerTools();
        }
        #endregion

        #region Private Methods
        internal IEnumerator WaitForFullRespawn()
        {
            yield return new WaitUntil(() => !IsPlayerDead());
            yield return new WaitForSeconds(0.5f);
            
            if (IsPlayerOnGround() && !IsPlayerDead())
            {
                Log.Info("[DeathManager] 玩家完全重生");
                OnPlayerFullyRespawned?.Invoke();
            }
        }

        /// <summary>
        /// 等待梦境中的重生完成
        /// </summary>
        internal IEnumerator WaitForMemoryRespawn()
        {
            // 等待死亡状态结束
            yield return new WaitUntil(() => !IsPlayerDead());
            // 等待玩家落地
            yield return new WaitUntil(() => IsPlayerOnGround());
            yield return new WaitForSeconds(0.3f);
            
            if (!IsPlayerDead())
            {
                Log.Info("[DeathManager] 梦境中玩家完全重生");
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
                        // 梦境中死亡，启动协程等待重生完成后触发事件
                        Log.Info($"[DeathManager] 梦境中死亡，nonLethal={nonLethal}，等待重生完成");
                        DeathManager.Instance.StartCoroutine(DeathManager.Instance.WaitForMemoryRespawn());
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
