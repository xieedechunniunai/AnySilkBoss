using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using GlobalSettings;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours.Common;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 丝球管理器 - 负责创建和管理自定义丝球预制体
    /// 统一版本：使用单一对象池管理所有丝球
    /// </summary>
    internal class SilkBallManager : MonoBehaviour
    {
        #region Fields
        private GameObject? _customSilkBallPrefab;
        public GameObject? CustomSilkBallPrefab => _customSilkBallPrefab;

        private AssetManager? _assetManager;
        private bool _initialized = false;

        // 全局静态缓存（所有丝球共享，避免重复查找）
        private static Transform? _cachedHeroTransform;
        private static GameObject? _cachedManagerObject;
        private static DamageHero? _cachedOriginalDamageHero;

        // Reaper 护符状态缓存
        private static bool _isReaperCrestEquipped = false;

        // 公开属性供丝球访问
        public static Transform? CachedHeroTransform => _cachedHeroTransform;
        public static GameObject? CachedManagerObject => _cachedManagerObject;
        public static DamageHero? CachedOriginalDamageHero => _cachedOriginalDamageHero;

        /// <summary>
        /// 获取 Reaper 护符是否装备
        /// </summary>
        public static bool IsReaperCrestEquipped => _isReaperCrestEquipped;

        /// <summary>
        /// 更新 Reaper 护符状态（由 Harmony 补丁调用）
        /// </summary>
        public static void UpdateReaperCrestState()
        {
            try
            {
                _isReaperCrestEquipped = Gameplay.ReaperCrest.IsEquipped;
                Log.Info($"[SilkBallManager] Reaper 护符状态更新: {_isReaperCrestEquipped}");
            }
            catch (System.Exception ex)
            {
                Log.Warn($"[SilkBallManager] 无法获取 Reaper 护符状态: {ex.Message}");
                _isReaperCrestEquipped = false;
            }
        }

        /// <summary>
        /// 清空所有静态缓存（回到主菜单时调用）
        /// </summary>
        public static void ClearAllStaticCaches()
        {
            Log.Info("[SilkBallManager] 清空所有静态缓存...");
            _cachedHeroTransform = null;
            _cachedManagerObject = null;
            _cachedOriginalDamageHero = null;
            _isReaperCrestEquipped = false;
        }

        // 音效参数缓存
        private FsmObject? _initAudioTable;
        private FsmObject? _initAudioPlayerPrefab;
        private FsmObject? _getSilkAudioTable;
        private FsmObject? _getSilkAudioPlayerPrefab;

        public FsmObject? InitAudioTable => _initAudioTable;
        public FsmObject? InitAudioPlayerPrefab => _initAudioPlayerPrefab;
        public FsmObject? GetSilkAudioTable => _getSilkAudioTable;
        public FsmObject? GetSilkAudioPlayerPrefab => _getSilkAudioPlayerPrefab;

        // 统一对象池
        private readonly List<SilkBallBehavior> _silkBallPool = new List<SilkBallBehavior>();
        private GameObject? _poolContainer;

        // 自动补充池机制
        private bool _enableAutoPooling = false;
        private const int MIN_POOL_SIZE = 160;  // 使用梦境池大小，确保足够容量
        private const float POOL_GENERATION_INTERVAL = 0.1f;

        private int _runtimeInstantiateCount = 0;
        #endregion

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private void OnDestroy()
        {
            CleanupAllSilkBallsOnDestroy();
        }

        private void OnDisable()
        {
            CleanupAllSilkBallsOnDestroy();
        }

        #region Initialization
        /// <summary>
        /// 初始化丝球管理器
        /// </summary>
        private IEnumerator Initialize()
        {
            if (_initialized)
            {
                Log.Info("SilkBallManager already initialized.");
                yield break;
            }

            _assetManager = GetComponent<AssetManager>();
            if (_assetManager == null)
            {
                Log.Error("无法找到 AssetManager 组件");
                yield break;
            }
            
            CacheGlobalReferences();
            
            yield return CreateCustomSilkBallPrefab();

            _initialized = true;
            Log.Info("SilkBallManager initialization completed.");

            // 自动初始化统一池
            InitializePool();

            // 启动自动补充池机制
            StartCoroutine(AutoPoolGeneration());
        }

        /// <summary>
        /// 初始化统一对象池
        /// </summary>
        private void InitializePool()
        {
            Log.Info("[SilkBallManager] 开始初始化统一池子...");

            // 创建池容器
            _poolContainer = new GameObject("SilkBall Pool");
            _poolContainer.transform.SetParent(transform);

            // 启用自动补充
            _enableAutoPooling = true;

            Log.Info($"[SilkBallManager] 统一池子已初始化，目标大小: {MIN_POOL_SIZE}");
        }

        /// <summary>
        /// 缓存全局引用（一次性获取，供所有丝球共享）
        /// </summary>
        private void CacheGlobalReferences()
        {
            Log.Info("[SilkBallManager] 开始缓存全局引用...");

            var hero = FindFirstObjectByType<HeroController>();
            if (hero != null)
            {
                _cachedHeroTransform = hero.transform;
                Log.Info($"[SilkBallManager] 已缓存玩家Transform: {hero.name}");
            }
            else
            {
                Log.Warn("[SilkBallManager] 未找到玩家（HeroController），玩家Transform缓存失败");
            }

            _cachedManagerObject = gameObject;
            Log.Info($"[SilkBallManager] 已缓存管理器GameObject: {gameObject.name}");

            var damageHeroEventManager = GetComponent<DamageHeroEventManager>();
            if (damageHeroEventManager != null)
            {
                if (damageHeroEventManager.IsInitialized() && damageHeroEventManager.HasDamageHero())
                {
                    _cachedOriginalDamageHero = damageHeroEventManager.DamageHero;
                    Log.Info("[SilkBallManager] 已缓存原始DamageHero引用");
                }
                else
                {
                    Log.Warn("[SilkBallManager] DamageHeroEventManager 未初始化或无DamageHero，稍后再尝试");
                }
            }
            else
            {
                Log.Warn("[SilkBallManager] 未找到 DamageHeroEventManager 组件");
            }

            // 初始化 Reaper 护符状态
            UpdateReaperCrestState();
        }

        /// <summary>
        /// 创建自定义丝球预制体
        /// </summary>
        private IEnumerator CreateCustomSilkBallPrefab()
        {
            Log.Info("=== 开始创建自定义丝球预制体 ===");

            var originalPrefab = _assetManager?.Get<GameObject>("Reaper Silk Bundle");
            if (originalPrefab == null)
            {
                Log.Error("无法获取原版丝球预制体 'Reaper Silk Bundle'");
                yield break;
            }

            Log.Info($"成功获取原版丝球预制体: {originalPrefab.name}");

            _customSilkBallPrefab = Object.Instantiate(originalPrefab);
            _customSilkBallPrefab.name = "Custom Silk Ball Prefab";
            _customSilkBallPrefab.transform.SetParent(transform);
            _customSilkBallPrefab.SetActive(false);

            Log.Info("丝球预制体复制完成，开始处理组件...");

            ProcessRootComponents();
            ProcessChildObjects();
            ExtractAudioActions();
            RemoveOriginalFSM();

            Log.Info($"=== 自定义丝球预制体创建完成: {_customSilkBallPrefab.name} ===");
            yield return null;
        }

        /// <summary>
        /// 处理根物体组件
        /// </summary>
        private void ProcessRootComponents()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 处理根物体组件 ---");

            var rb2d = _customSilkBallPrefab.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                Log.Info($"保留 Rigidbody2D: gravityScale={rb2d.gravityScale}, linearDamping={rb2d.linearDamping}");
            }

            var objectBounce = _customSilkBallPrefab.GetComponent<ObjectBounce>();
            if (objectBounce != null)
            {
                Object.Destroy(objectBounce);
                Log.Info("移除 ObjectBounce 组件");
            }

            var setZ = _customSilkBallPrefab.GetComponent<SetZ>();
            if (setZ != null)
            {
                Log.Info("保留 SetZ 组件");
            }

            var autoRecycle = _customSilkBallPrefab.GetComponent<AutoRecycleSelf>();
            if (autoRecycle != null)
            {
                Object.Destroy(autoRecycle);
                Log.Info("移除 AutoRecycleSelf 组件");
            }

            var eventRegisters = _customSilkBallPrefab.GetComponents<EventRegister>();
            foreach (var er in eventRegisters)
            {
                Object.Destroy(er);
                Log.Info($"移除 EventRegister 组件: {er.subscribedEvent}");
            }

            var bounceOnWater = _customSilkBallPrefab.GetComponent("bounceOnWater");
            if (bounceOnWater != null)
            {
                Object.Destroy(bounceOnWater);
                Log.Info("移除 bounceOnWater 组件");
            }
        }

        /// <summary>
        /// 处理子物体
        /// </summary>
        private void ProcessChildObjects()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 处理子物体 ---");

            Transform spriteSilk = _customSilkBallPrefab.transform.Find("Sprite Silk");
            if (spriteSilk != null)
            {
                Log.Info("找到 Sprite Silk 子物体，保留所有组件");

                var circleCollider = spriteSilk.GetComponent<CircleCollider2D>();
                if (circleCollider != null)
                {
                    Log.Info($"找到 CircleCollider2D: radius={circleCollider.radius}, isTrigger={circleCollider.isTrigger}");
                    circleCollider.isTrigger = true;
                }
            }

            Transform terrainCollider = _customSilkBallPrefab.transform.Find("Terrain Collider");
            if (terrainCollider != null)
            {
                terrainCollider.gameObject.SetActive(false);
                Log.Info("禁用 Terrain Collider 子物体");
            }
        }

        /// <summary>
        /// 提取音效参数
        /// </summary>
        private void ExtractAudioActions()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 提取音效参数 ---");

            var controlFsm = _customSilkBallPrefab.GetComponents<PlayMakerFSM>()
                .FirstOrDefault(fsm => fsm.FsmName == "Control");

            if (controlFsm == null)
            {
                Log.Warn("未找到原版 Control FSM，跳过音效提取");
                return;
            }

            ExtractPlayRandomAudioClipTableParams(controlFsm, "Init",
                out _initAudioTable, out _initAudioPlayerPrefab);

            ExtractPlayRandomAudioClipTableParams(controlFsm, "Get Silk",
                out _getSilkAudioTable, out _getSilkAudioPlayerPrefab);
        }

        private void ExtractPlayRandomAudioClipTableParams(PlayMakerFSM fsm, string stateName,
            out FsmObject? table, out FsmObject? audioPlayerPrefab)
        {
            table = null;
            audioPlayerPrefab = null;

            var state = fsm.FsmStates.FirstOrDefault(s => s.Name == stateName);
            if (state == null)
            {
                Log.Warn($"未找到状态: {stateName}");
                return;
            }

            var audioAction = state.Actions.FirstOrDefault(a => a.GetType().Name == "PlayRandomAudioClipTable");
            if (audioAction == null)
            {
                Log.Warn($"在状态 {stateName} 中未找到 PlayRandomAudioClipTable 动作");
                return;
            }

            var type = audioAction.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var tableField = type.GetField("Table", flags);
            var audioPrefabField = type.GetField("AudioPlayerPrefab", flags);

            if (tableField != null)
            {
                table = tableField.GetValue(audioAction) as FsmObject;
                Log.Info($"成功提取 {stateName} 的 Table 参数");
            }

            if (audioPrefabField != null)
            {
                audioPlayerPrefab = audioPrefabField.GetValue(audioAction) as FsmObject;
                Log.Info($"成功提取 {stateName} 的 AudioPlayerPrefab 参数");
            }
        }

        private void RemoveOriginalFSM()
        {
            if (_customSilkBallPrefab == null) return;

            var oldFSMs = _customSilkBallPrefab.GetComponents<PlayMakerFSM>();
            foreach (var fsm in oldFSMs)
            {
                Log.Info($"移除原版 FSM: {fsm.FsmName}");
                Object.Destroy(fsm);
            }
        }
        #endregion

        #region Pool Management
        /// <summary>
        /// 从对象池获取可用丝球（如果没有则创建新实例）
        /// </summary>
        private SilkBallBehavior? GetAvailableSilkBall()
        {
            var availableBall = _silkBallPool.FirstOrDefault(b =>
                b != null &&
                b.IsAvailable &&
                !b.isActive
            );

            if (availableBall != null)
            {
                return availableBall;
            }

            // 没有可用的，创建新实例
            int activeCount = _silkBallPool.Count(b => b != null && b.isActive);
            int availableCount = _silkBallPool.Count(b => b != null && b.IsAvailable);
            int enabledCount = _silkBallPool.Count(b => b != null && b.gameObject.activeSelf);

            _runtimeInstantiateCount++;
            Log.Warn($"[SilkBallManager] 池可用对象不足，运行时创建新实例({_runtimeInstantiateCount}) - 总数:{_silkBallPool.Count}, 激活:{activeCount}, 可用:{availableCount}, GameObject启用:{enabledCount}");

            return CreateNewSilkBallInstance();
        }

        /// <summary>
        /// 创建新的丝球实例并加入池中
        /// </summary>
        private SilkBallBehavior? CreateNewSilkBallInstance()
        {
            if (_customSilkBallPrefab == null)
            {
                Log.Error("自定义丝球预制体未初始化，无法创建实例");
                return null;
            }

            if (_poolContainer == null)
            {
                Log.Error("对象池容器未初始化，无法创建实例");
                return null;
            }

            var silkBallInstance = Object.Instantiate(_customSilkBallPrefab, _poolContainer.transform);
            silkBallInstance.name = $"Silk Ball #{_silkBallPool.Count}";

            var behavior = silkBallInstance.AddComponent<SilkBallBehavior>();
            if (behavior == null)
            {
                Log.Error("无法添加 SilkBallBehavior 组件！");
                Object.Destroy(silkBallInstance);
                return null;
            }

            behavior.InitializeOnce(_poolContainer.transform, this);

            _silkBallPool.Add(behavior);
            return behavior;
        }

        /// <summary>
        /// 生成并准备丝球（基础版本）
        /// </summary>
        public SilkBallBehavior? SpawnSilkBall(Vector3 position)
        {
            return SpawnSilkBall(position, 30f, 20f, 6f, 1f);
        }

        /// <summary>
        /// 生成并准备丝球（完整参数版本）
        /// </summary>
        public SilkBallBehavior? SpawnSilkBall(Vector3 position, float acceleration, float maxSpeed, float chaseTime, float scale, bool enableRotation = true, Transform? customTarget = null, bool ignoreWall = false, bool delayDamageActivation = true, bool canBeClearedByAttack = true)
        {
            var behavior = GetAvailableSilkBall();
            if (behavior == null)
            {
                Log.Error("无法获取可用丝球，生成失败");
                return null;
            }

            behavior.PrepareForUse(position, acceleration, maxSpeed, chaseTime, scale, enableRotation, customTarget, ignoreWall, delayDamageActivation, canBeClearedByAttack);
            return behavior;
        }

        /// <summary>
        /// 回收所有活跃的丝球到对象池
        /// </summary>
        public void RecycleAllActiveSilkBalls()
        {
            int recycledCount = 0;

            foreach (var behavior in _silkBallPool)
            {
                if (behavior != null && behavior.isActive)
                {
                    behavior.RecycleToPool();
                    recycledCount++;
                }
            }

            Log.Info($"已回收所有活跃丝球到对象池，共 {recycledCount} 个");
        }

        /// <summary>
        /// 查看对象池状态（调试用）
        /// </summary>
        public void LogPoolStatus()
        {
            int available = _silkBallPool.Count(b => b != null && b.IsAvailable);
            int active = _silkBallPool.Count(b => b != null && b.isActive);
            int enabled = _silkBallPool.Count(b => b != null && b.gameObject.activeSelf);

            Log.Info("=== 丝球对象池状态 ===");
            Log.Info($"  总数: {_silkBallPool.Count}");
            Log.Info($"  可用: {available}");
            Log.Info($"  活跃中: {active}");
            Log.Info($"  GameObject启用: {enabled}");
            Log.Info($"  运行时创建次数: {_runtimeInstantiateCount}");
        }

        /// <summary>
        /// 自动补充对象池机制
        /// </summary>
        private IEnumerator AutoPoolGeneration()
        {
            while (true)
            {
                yield return new WaitForSeconds(POOL_GENERATION_INTERVAL);

                if (!_enableAutoPooling)
                {
                    continue;
                }

                if (!_initialized || _customSilkBallPrefab == null || _poolContainer == null)
                {
                    continue;
                }

                int currentPoolSize = _silkBallPool.Count(b => b != null);

                if (currentPoolSize < MIN_POOL_SIZE)
                {
                    var newBall = CreateNewSilkBallInstance();
                    if (newBall != null)
                    {
                        newBall.RecycleToPool();
                    }
                }
            }
        }
        #endregion

        #region Cleanup
        /// <summary>
        /// 场景切换或销毁时的完全清理
        /// </summary>
        private void CleanupAllSilkBallsOnDestroy()
        {
            Log.Info("SilkBallManager场景切换/销毁，执行完全清理");

            StopAllCoroutines();
            _enableAutoPooling = false;

            RecycleAllActiveSilkBalls();

            if (_silkBallPool != null)
            {
                _silkBallPool.Clear();
                Log.Info("已清空丝球对象池");
            }

            if (_poolContainer != null)
            {
                UnityEngine.Object.Destroy(_poolContainer);
                _poolContainer = null;
                Log.Info("已销毁丝球对象池容器");
            }

            _initialized = false;
        }

        /// <summary>
        /// 回到主菜单时的完全重置
        /// </summary>
        public void ResetOnReturnToMenu()
        {
            Log.Info("[SilkBallManager] 回到主菜单，执行完全重置...");

            StopAllCoroutines();
            _enableAutoPooling = false;

            _silkBallPool.Clear();
            if (_poolContainer != null)
            {
                UnityEngine.Object.Destroy(_poolContainer);
                _poolContainer = null;
            }

            ClearAllStaticCaches();
            _runtimeInstantiateCount = 0;
            _initialized = false;

            Log.Info("[SilkBallManager] 完全重置完成，等待重新进入游戏场景");
        }

        /// <summary>
        /// 重新进入游戏场景时的初始化
        /// </summary>
        public void ReinitializeOnEnterGame()
        {
            Log.Info("[SilkBallManager] 重新进入游戏，开始重新初始化...");

            if (_initialized)
            {
                Log.Info("[SilkBallManager] 已初始化，跳过重新初始化");
                return;
            }

            CacheGlobalReferences();
            StartCoroutine(ReinitializeCoroutine());
        }

        private IEnumerator ReinitializeCoroutine()
        {
            yield return null;

            if (_customSilkBallPrefab != null)
            {
                _initialized = true;
                Log.Info("[SilkBallManager] 预制体仍有效，直接初始化池子");

                InitializePool();
                StartCoroutine(AutoPoolGeneration());
            }
            else
            {
                Log.Warn("[SilkBallManager] 预制体丢失，执行完全重新初始化");
                StartCoroutine(Initialize());
            }
        }
        #endregion
    }
}
