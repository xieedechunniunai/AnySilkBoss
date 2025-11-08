using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 死亡管理器
    /// 使用Harmony补丁拦截游戏的死亡和重生机制
    /// 提供可订阅的事件供其他模块使用
    /// </summary>
    internal class DeathManager : MonoBehaviour
    {
        #region Singleton Implementation
        public static DeathManager Instance { get; private set; }

        /// <summary>
        /// 当玩家完全重生后触发（已落地且不在死亡状态）
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
            Log.Info("死亡管理器已初始化");
        }

        private void OnEnable()
        {
            // 延迟应用Harmony补丁，确保游戏完全初始化
            StartCoroutine(DelayedPatchApplication());
        }

        private void OnDisable()
        {
            // 卸载Harmony补丁
            try
            {
                var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID + ".DeathManager");
                harmony.UnpatchSelf();
                Log.Info("DeathManager Harmony补丁已卸载");
            }
            catch (Exception ex)
            {
                Log.Error($"卸载DeathManager补丁失败: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private IEnumerator DelayedPatchApplication()
        {
            // 等待游戏完全初始化
            yield return new WaitForSeconds(2f);
            
            try
            {
                var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID + ".DeathManager");
                harmony.PatchAll(typeof(HeroControllerDeathPatches));
                Log.Info("DeathManager Harmony补丁已应用");
            }
            catch (Exception ex)
            {
                Log.Error($"应用DeathManager补丁失败: {ex.Message}");
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 内部方法：触发玩家死亡事件
        /// 由Harmony补丁调用
        /// </summary>
        internal void TriggerPlayerDied()
        {
            Log.Info("检测到玩家死亡");
            OnPlayerDied?.Invoke();
        }

        /// <summary>
        /// 内部方法：触发危险重生开始事件
        /// 由Harmony补丁调用
        /// </summary>
        internal void TriggerHazardRespawnStart()
        {
            Log.Info("检测到危险重生开始");
            OnHazardRespawnStart?.Invoke();
            
            // 启动重生检测协程
            StartCoroutine(WaitForFullRespawn());
        }

        /// <summary>
        /// 手动触发重生事件（用于测试）
        /// </summary>
        public void ManualTriggerRespawn()
        {
            Log.Info("手动触发重生事件");
            OnPlayerFullyRespawned?.Invoke();
        }
        #endregion

        #region Private Implementation
        /// <summary>
        /// 等待玩家完全重生的协程
        /// </summary>
        private IEnumerator WaitForFullRespawn()
        {
            // 等待玩家不再处于死亡状态
            yield return new WaitUntil(() => !IsPlayerDead());
            
            // 额外等待确保玩家已经稳定落地
            yield return new WaitForSeconds(0.5f);
            
            // 再次检查玩家是否在地面上
            if (IsPlayerOnGround() && !IsPlayerDead())
            {
                Log.Info("玩家完全重生");
                OnPlayerFullyRespawned?.Invoke();
            }
        }

        /// <summary>
        /// 检查玩家是否死亡
        /// </summary>
        private bool IsPlayerDead()
        {
            try
            {
                if (HeroController.instance == null) return false;
                return HeroController.instance.cState.dead;
            }
            catch (Exception ex)
            {
                Log.Error($"检查玩家死亡状态失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查玩家是否在地面上
        /// </summary>
        private bool IsPlayerOnGround()
        {
            try
            {
                if (HeroController.instance == null) return false;
                return HeroController.instance.cState.onGround;
            }
            catch (Exception ex)
            {
                Log.Error($"检查玩家地面状态失败: {ex.Message}");
                return false;
            }
        }
        #endregion
    }

    /// <summary>
    /// Harmony补丁：拦截HeroController的死亡和重生方法
    /// </summary>
    [HarmonyPatch(typeof(HeroController))]
    internal static class HeroControllerDeathPatches
    {
        /// <summary>
        /// 拦截危险重生协程的开始
        /// HazardRespawn 是游戏处理危险（尖刺、酸液等）导致重生的协程
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("HazardRespawn")]
        private static void OnHazardRespawnStart(HeroController __instance)
        {
            try
            {
                if (DeathManager.Instance != null)
                {
                    DeathManager.Instance.TriggerHazardRespawnStart();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"HazardRespawn 补丁执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 拦截玩家死亡状态的设置
        /// 通过监视 cState.dead 的变化来检测死亡
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("Die")]
        private static void OnPlayerDie(HeroController __instance)
        {
            try
            {
                if (DeathManager.Instance != null)
                {
                    DeathManager.Instance.TriggerPlayerDied();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Die 补丁执行失败: {ex.Message}");
            }
        }

    }
}
