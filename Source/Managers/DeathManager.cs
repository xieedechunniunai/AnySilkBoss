using System;
using System.Collections;
using UnityEngine;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 死亡管理器
    /// 
    /// 功能：
    /// 1. 监听梦境模式下玩家死亡和重生事件
    /// 2. 在重生后自动恢复工具和贝壳碎片
    /// 
    /// 注意：Harmony 补丁已移至 Source/Patches/DeathPatches.cs
    /// </summary>
    internal class DeathManager : MonoBehaviour
    {
        #region Singleton
        public static DeathManager? Instance { get; private set; }

        /// <summary>
        /// 当玩家完全重生后触发
        /// </summary>
        public event Action? OnPlayerFullyRespawned;

        /// <summary>
        /// 标记是否正在等待梦境重生
        /// </summary>
        private bool _waitingForMemoryRespawn = false;

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
            // 订阅场景进入完成事件
            if (GameManager.instance != null)
            {
                GameManager.instance.OnFinishedEnteringScene += OnSceneEntered;
            }
        }

        private void OnDisable()
        {
            // 取消订阅场景事件
            if (GameManager.instance != null)
            {
                GameManager.instance.OnFinishedEnteringScene -= OnSceneEntered;
            }
        }

        private void OnDestroy()
        {
            // 取消订阅
            OnPlayerFullyRespawned -= RestorePlayerTools;
            
            if (Instance == this) Instance = null;
        }
        #endregion

        #region Scene Event Handler
        /// <summary>
        /// 场景进入完成时的回调
        /// </summary>
        private void OnSceneEntered()
        {
            if (_waitingForMemoryRespawn && MemoryManager.IsInMemoryMode)
            {
                _waitingForMemoryRespawn = false;
                Log.Info("[DeathManager] 梦境中玩家完全重生（通过 OnFinishedEnteringScene）");
                OnPlayerFullyRespawned?.Invoke();
            }
        }

        /// <summary>
        /// 设置等待梦境重生标志（供补丁调用）
        /// </summary>
        internal void SetWaitingForMemoryRespawn()
        {
            _waitingForMemoryRespawn = true;
            Log.Info("[DeathManager] 设置等待梦境重生标志");
        }
        #endregion

        #region Public Methods
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
    }
}
