using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours.Normal;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// LaceCircleSlash 管理器
    /// 负责创建和管理 lace_circle_slash 物品的实例
    /// 不使用对象池，而是直接复制2个实例到场景中进行复用
    /// </summary>
    internal class LaceCircleSlashManager : MonoBehaviour
    {
        #region 常量配置
        private const string BossSceneName = "Cradle_03";
        private const int InstanceCount = 5;          // 实例数量
        private const float ScaleMultiplier = 2f;     // 缩放倍数
        private const float DeactivateTime = 2f;      // 自动禁用时间
        #endregion

        #region 私有字段
        private List<LaceCircleSlashBehavior> _instances = new List<LaceCircleSlashBehavior>();
        private AssetManager? _assetManager;
        private bool _initialized = false;
        #endregion

        #region Unity 生命周期
        private void OnEnable()
        {
            // 监听场景加载事件
            SceneManager.sceneLoaded += OnSceneLoaded;
            // 监听场景切换事件（离开场景）
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDisable()
        {
            // 取消监听场景加载事件
            SceneManager.sceneLoaded -= OnSceneLoaded;
            // 取消监听场景切换事件
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void Start()
        {
            // 获取同一 GameObject 上的 AssetManager 组件
            _assetManager = GetComponent<AssetManager>();
            if (_assetManager == null)
            {
                Log.Error("LaceCircleSlashManager: 无法找到 AssetManager 组件");
            }
        }
        #endregion

        #region 场景事件处理
        /// <summary>
        /// 场景加载回调（进入场景）
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 只在 BOSS 场景时处理
            if (scene.name != BossSceneName)
            {
                return;
            }

            // 每次进入 BOSS 场景都重新初始化
            Log.Info($"LaceCircleSlashManager: 检测到 BOSS 场景 {scene.name}，开始初始化...");
            StartCoroutine(Initialize());
        }

        /// <summary>
        /// 场景切换回调（离开场景）
        /// </summary>
        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            // 只有真正离开 BOSS 场景（到其他场景）时才清理，同场景重载（如死亡复活）不清理
            if (oldScene.name == BossSceneName && newScene.name != BossSceneName)
            {
                Log.Info($"LaceCircleSlashManager: 离开 BOSS 场景 {oldScene.name}，清理实例");
                Cleanup();
            }
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化管理器，创建实例
        /// </summary>
        private IEnumerator Initialize()
        {
            // 等待一帧，确保场景加载完成
            yield return null;

            // 确保 AssetManager 已获取
            if (_assetManager == null)
            {
                _assetManager = GetComponent<AssetManager>();
                if (_assetManager == null)
                {
                    Log.Error("LaceCircleSlashManager: 无法找到 AssetManager 组件，初始化失败");
                    yield break;
                }
            }

            // 获取原始 lace_circle_slash 资源
            var originalPrefab = _assetManager.Get<GameObject>("lace_circle_slash");
            if (originalPrefab == null)
            {
                Log.Error("LaceCircleSlashManager: 无法从 AssetManager 获取 lace_circle_slash");
                yield break;
            }

            Log.Info($"LaceCircleSlashManager: 成功获取 lace_circle_slash，原始大小: {originalPrefab.transform.localScale}");

            // 创建指定数量的实例
            for (int i = 0; i < InstanceCount; i++)
            {
                var instance = CreateInstance(originalPrefab, i);
                if (instance != null)
                {
                    _instances.Add(instance);
                }
            }

            _initialized = true;
            Log.Info($"LaceCircleSlashManager: 初始化完成，创建了 {_instances.Count} 个实例");
        }

        /// <summary>
        /// 创建单个实例
        /// </summary>
        private LaceCircleSlashBehavior? CreateInstance(GameObject originalPrefab, int index)
        {
            // 复制物体
            var instanceObj = Object.Instantiate(originalPrefab);
            instanceObj.name = $"LaceCircleSlash_Instance_{index}";
            
            // 默认禁用
            instanceObj.SetActive(false);

            // 添加 Behavior 组件
            var behavior = instanceObj.GetComponent<LaceCircleSlashBehavior>();
            if (behavior == null)
            {
                behavior = instanceObj.AddComponent<LaceCircleSlashBehavior>();
            }

            // 配置 Behavior
            behavior.scaleMultiplier = ScaleMultiplier;
            behavior.deactivateTime = DeactivateTime;

            Log.Info($"LaceCircleSlashManager: 创建实例 {instanceObj.name}");
            return behavior;
        }
        #endregion

        #region 公开方法
        /// <summary>
        /// 在指定位置生成 LaceCircleSlash（使用默认缩放倍数）
        /// 会找到一个未激活的实例，设置位置并激活
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <returns>是否成功生成</returns>
        public bool SpawnLaceCircleSlash(Vector3 position)
        {
            return SpawnLaceCircleSlash(position, ScaleMultiplier);
        }

        /// <summary>
        /// 在指定位置生成 LaceCircleSlash（指定缩放倍数）
        /// 会找到一个未激活的实例，设置位置并激活
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <param name="scaleMultiplier">缩放倍数</param>
        /// <returns>是否成功生成</returns>
        public bool SpawnLaceCircleSlash(Vector3 position, float scaleMultiplier)
        {
            if (!_initialized || _instances.Count == 0)
            {
                Log.Warn("LaceCircleSlashManager: 未初始化或没有可用实例");
                return false;
            }

            // 查找一个未激活的实例
            var availableInstance = _instances.FirstOrDefault(i => i != null && !i.gameObject.activeSelf);
            
            if (availableInstance == null)
            {
                Log.Warn("LaceCircleSlashManager: 没有可用的实例（全部都在使用中）");
                return false;
            }

            // 设置位置
            availableInstance.transform.position = position;
            
            // 设置缩放倍数
            availableInstance.SetScaleMultiplier(scaleMultiplier);
            
            // 重置定时器
            availableInstance.ResetTimer();
            
            // 激活
            availableInstance.gameObject.SetActive(true);

            Log.Info($"LaceCircleSlashManager: 在位置 {position} 生成 LaceCircleSlash (缩放: {scaleMultiplier}x)");
            return true;
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized()
        {
            return _initialized;
        }

        /// <summary>
        /// 强制禁用所有活跃的实例
        /// </summary>
        public void DeactivateAll()
        {
            foreach (var instance in _instances)
            {
                if (instance != null && instance.gameObject.activeSelf)
                {
                    instance.gameObject.SetActive(false);
                }
            }
            Log.Info("LaceCircleSlashManager: 已禁用所有活跃实例");
        }
        #endregion

        #region 清理
        /// <summary>
        /// 清理所有实例（离开场景时调用）
        /// </summary>
        public void Cleanup()
        {
            Log.Info("LaceCircleSlashManager: 开始清理...");

            // 停止所有协程
            StopAllCoroutines();

            // 销毁所有实例
            foreach (var instance in _instances)
            {
                if (instance != null && instance.gameObject != null)
                {
                    Object.Destroy(instance.gameObject);
                }
            }

            // 清空列表
            _instances.Clear();

            // 重置状态
            _initialized = false;

            Log.Info("LaceCircleSlashManager: 清理完成");
        }
        #endregion
    }
}

