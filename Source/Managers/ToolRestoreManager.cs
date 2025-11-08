using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 工具恢复管理器
    /// 负责在玩家重生后恢复工具
    /// </summary>
    internal class ToolRestoreManager : MonoBehaviour
    {
        public static ToolRestoreManager Instance { get; private set; }

        private void Awake()
        {
            // 设置单例实例
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // 订阅死亡管理器的重生事件
            if (DeathManager.Instance != null)
            {
                DeathManager.Instance.OnPlayerFullyRespawned += OnPlayerFullyRespawned;
            }
            else
            {
                // 如果死亡管理器还未创建，延迟订阅
                StartCoroutine(DelayedSubscribe());
            }

            Log.Info("工具恢复管理器已初始化");
        }

        private void OnDestroy()
        {
            // 取消订阅
            if (DeathManager.Instance != null)
            {
                DeathManager.Instance.OnPlayerFullyRespawned -= OnPlayerFullyRespawned;
            }
        }

        /// <summary>
        /// 延迟订阅死亡管理器事件
        /// </summary>
        private IEnumerator DelayedSubscribe()
        {
            // 等待死亡管理器初始化
            while (DeathManager.Instance == null)
            {
                yield return null;
            }

            // 订阅事件
            DeathManager.Instance.OnPlayerFullyRespawned += OnPlayerFullyRespawned;
            Log.Info("成功订阅死亡管理器的重生事件");
        }

        /// <summary>
        /// 当玩家完全重生时的回调
        /// </summary>
        private void OnPlayerFullyRespawned()
        {
            Log.Info("接收到玩家重生事件，开始恢复道具...");
            RestorePlayerTools();
        }

        /// <summary>
        /// 恢复玩家道具
        /// </summary>
        private void RestorePlayerTools()
        {
            try
            {
                Log.Info("尝试调用ToolItemManager.TryReplenishTools...");
                
                // 恢复工具
                ToolItemManager.TryReplenishTools(true, ToolItemManager.ReplenishMethod.QuickCraft);
                
                // 恢复贝壳碎片
                GameManager.instance.playerData.ShellShards = 700;

                Log.Info("道具恢复完成");
            }
            catch (Exception ex)
            {
                Log.Error($"恢复道具失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 公开方法：手动触发道具恢复
        /// </summary>
        public void ManualRestoreTools()
        {
            Log.Info("手动触发道具恢复");
            RestorePlayerTools();
        }

        /// <summary>
        /// 公开方法：恢复特定工具
        /// 可以在未来扩展以支持恢复特定类型的工具
        /// </summary>
        public void RestoreSpecificTool(string toolName, int amount = 1)
        {
            try
            {
                // TODO: 实现特定工具恢复逻辑
                Log.Info($"恢复特定工具: {toolName} x{amount}");
            }
            catch (Exception ex)
            {
                Log.Error($"恢复特定工具失败: {ex.Message}");
            }
        }
    }
}