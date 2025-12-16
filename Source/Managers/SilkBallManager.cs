using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours.Normal;
using AnySilkBoss.Source.Behaviours.Memory;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 丝球管理器 - 负责创建和管理自定义丝球预制体
    /// 作为普通组件挂载到 AnySilkBossManager 上
    /// </summary>
    internal class SilkBallManager : MonoBehaviour
    {
        #region Fields
        private GameObject? _customSilkBallPrefab;
        public GameObject? CustomSilkBallPrefab => _customSilkBallPrefab;

        private AssetManager? _assetManager;
        private bool _initialized = false;

        // 音效参数缓存
        private FsmObject? _initAudioTable;
        private FsmObject? _initAudioPlayerPrefab;
        private FsmObject? _getSilkAudioTable;
        private FsmObject? _getSilkAudioPlayerPrefab;

        public FsmObject? InitAudioTable => _initAudioTable;
        public FsmObject? InitAudioPlayerPrefab => _initAudioPlayerPrefab;
        public FsmObject? GetSilkAudioTable => _getSilkAudioTable;
        public FsmObject? GetSilkAudioPlayerPrefab => _getSilkAudioPlayerPrefab;

        // 对象池 - 普通版本
        private readonly List<SilkBallBehavior> _silkBallPool = new List<SilkBallBehavior>();
        private GameObject? _poolContainer;

        // 对象池 - Memory 版本
        private readonly List<MemorySilkBallBehavior> _memorySilkBallPool = new List<MemorySilkBallBehavior>();
        private GameObject? _memoryPoolContainer;

        // 自动补充池机制
        private bool _enableNormalAutoPooling = false;  // 普通池自动补充
        private bool _enableMemoryAutoPooling = false;  // 梦境池自动补充
        private const int NORMAL_MIN_POOL_SIZE = 120;   // 普通池大小
        private const int MEMORY_MIN_POOL_SIZE = 160;   // 梦境池大小（扩大到160）
        private const float POOL_GENERATION_INTERVAL = 0.1f;

        // 池子加载状态
        private bool _normalPoolLoaded = false;
        private bool _memoryPoolLoaded = false;
        #endregion

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private void OnDestroy()
        {
            // 场景切换或对象销毁时清理所有丝球
            CleanupAllSilkBallsOnDestroy();
        }

        private void OnDisable()
        {
            // 对象禁用时也清理丝球
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

            // 获取同一 GameObject 上的 AssetManager 组件
            _assetManager = GetComponent<AssetManager>();
            if (_assetManager == null)
            {
                Log.Error("无法找到 AssetManager 组件");
                yield break;
            }
            yield return CreateCustomSilkBallPrefab();
            CreatePoolContainer();

            _initialized = true;
            Log.Info("SilkBallManager initialization completed.");

            // 默认只加载普通池子
            LoadNormalPool();

            // 启动自动补充池机制（会根据各自的启用标记决定是否工作）
            StartCoroutine(AutoPoolGeneration());
            StartCoroutine(AutoMemoryPoolGeneration());
        }

        /// <summary>
        /// 创建对象池容器（只创建结构，不加载池子内容）
        /// </summary>
        private void CreatePoolContainer()
        {
            // 容器在需要时按需创建，这里只做初始化准备
            Log.Info("对象池容器初始化准备完成");
        }


        /// <summary>
        /// 创建自定义丝球预制体
        /// </summary>
        private IEnumerator CreateCustomSilkBallPrefab()
        {
            Log.Info("=== 开始创建自定义丝球预制体 ===");

            // 获取原版丝球预制体
            var originalPrefab = _assetManager?.Get<GameObject>("Reaper Silk Bundle");
            if (originalPrefab == null)
            {
                Log.Error("无法获取原版丝球预制体 'Reaper Silk Bundle'");
                yield break;
            }

            Log.Info($"成功获取原版丝球预制体: {originalPrefab.name}");

            // 复制一份预制体
            _customSilkBallPrefab = Object.Instantiate(originalPrefab);
            _customSilkBallPrefab.name = "Custom Silk Ball Prefab";

            // 不需要 DontDestroyOnLoad，因为 SilkBallManager 已经在 AnySilkBossManager 上，后者已设置 DontDestroyOnLoad
            // 预制体作为 Manager 的子物体保存
            _customSilkBallPrefab.transform.SetParent(transform);

            // 禁用该对象（作为预制体模板）
            _customSilkBallPrefab.SetActive(false);

            Log.Info("丝球预制体复制完成，开始处理组件...");

            // 处理根物体组件
            ProcessRootComponents();

            // 处理子物体
            ProcessChildObjects();

            // 提取音效动作（必须在删除原版 FSM 之前）
            ExtractAudioActions();

            // 删除原版 PlayMakerFSM 组件
            RemoveOriginalFSM();

            // ⚠️ 不在预制体上添加任何 Behavior 组件
            // Behavior 组件会在创建实例时根据需要添加（普通版或 Memory 版）
            // 这样可以避免预制体初始化时添加的子组件（如 CollisionForwarder）被重复添加

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

            // 保留 Rigidbody2D（会在 SilkBallBehavior 中重新配置）
            var rb2d = _customSilkBallPrefab.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                Log.Info($"保留 Rigidbody2D: gravityScale={rb2d.gravityScale}, linearDamping={rb2d.linearDamping}");
            }

            // 移除不需要的组件
            // ObjectBounce - 原版的弹跳逻辑，我们不需要
            var objectBounce = _customSilkBallPrefab.GetComponent<ObjectBounce>();
            if (objectBounce != null)
            {
                Object.Destroy(objectBounce);
                Log.Info("移除 ObjectBounce 组件");
            }

            // SetZ - 保持Z轴位置，可以保留
            var setZ = _customSilkBallPrefab.GetComponent<SetZ>();
            if (setZ != null)
            {
                Log.Info("保留 SetZ 组件");
            }

            // AutoRecycleSelf - 自动回收，我们用自己的销毁逻辑
            var autoRecycle = _customSilkBallPrefab.GetComponent<AutoRecycleSelf>();
            if (autoRecycle != null)
            {
                Object.Destroy(autoRecycle);
                Log.Info("移除 AutoRecycleSelf 组件");
            }

            // EventRegister - 事件注册，可以移除
            var eventRegisters = _customSilkBallPrefab.GetComponents<EventRegister>();
            foreach (var er in eventRegisters)
            {
                Object.Destroy(er);
                Log.Info($"移除 EventRegister 组件: {er.subscribedEvent}");
            }

            // bounceOnWater - 水面弹跳，可以移除
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

            // 重要子物体：Sprite Silk（主要的可视化部分）
            Transform spriteSilk = _customSilkBallPrefab.transform.Find("Sprite Silk");
            if (spriteSilk != null)
            {
                Log.Info("找到 Sprite Silk 子物体，保留所有组件");
                // 保留所有组件：MeshFilter, MeshRenderer, tk2dSprite, tk2dSpriteAnimator, 
                // RandomRotation, RandomScale, CircleCollider2D, AmbientSway, NonBouncer

                // 获取 CircleCollider2D 用于伤害检测
                var circleCollider = spriteSilk.GetComponent<CircleCollider2D>();
                if (circleCollider != null)
                {
                    Log.Info($"找到 CircleCollider2D: radius={circleCollider.radius}, isTrigger={circleCollider.isTrigger}");
                    // 确保是触发器
                    circleCollider.isTrigger = true;
                }
            }
            // Terrain Collider - 地形碰撞，可以移除或禁用
            Transform terrainCollider = _customSilkBallPrefab.transform.Find("Terrain Collider");
            if (terrainCollider != null)
            {
                terrainCollider.gameObject.SetActive(false);
                Log.Info("禁用 Terrain Collider 子物体（使用 Sprite Silk 的碰撞器）");
            }
        }

        /// <summary>
        /// 提取音效参数
        /// </summary>
        private void ExtractAudioActions()
        {
            if (_customSilkBallPrefab == null) return;

            Log.Info("--- 提取音效参数 ---");

            // 获取原版 Control FSM
            var controlFsm = _customSilkBallPrefab.GetComponents<PlayMakerFSM>()
                .FirstOrDefault(fsm => fsm.FsmName == "Control");

            if (controlFsm == null)
            {
                Log.Warn("未找到原版 Control FSM，跳过音效提取");
                return;
            }

            // 提取 Init 状态的 PlayRandomAudioClipTable 参数
            ExtractPlayRandomAudioClipTableParams(controlFsm, "Init",
                out _initAudioTable, out _initAudioPlayerPrefab);

            // 提取 Get Silk 状态的 PlayRandomAudioClipTable 参数
            ExtractPlayRandomAudioClipTableParams(controlFsm, "Get Silk",
                out _getSilkAudioTable, out _getSilkAudioPlayerPrefab);
        }

        /// <summary>
        /// 从 PlayRandomAudioClipTable 动作中提取参数
        /// </summary>
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

            // 查找 PlayRandomAudioClipTable 动作
            var audioAction = state.Actions.FirstOrDefault(a => a.GetType().Name == "PlayRandomAudioClipTable");
            if (audioAction == null)
            {
                Log.Warn($"在状态 {stateName} 中未找到 PlayRandomAudioClipTable 动作");
                return;
            }

            // 通过反射获取字段
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


        /// <summary>
        /// 移除原版 FSM
        /// </summary>
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

        /// <summary>
        /// 从对象池获取可用丝球（如果没有则创建新实例）
        /// ⚠️ 关键修复：只从明确被回收的丝球中获取（IsAvailable=true且isActive=false）
        /// 不会复用场上还在活动的丝球，避免大丝球爆炸时复用其他丝球
        /// </summary>
        private SilkBallBehavior? GetAvailableSilkBall()
        {
            // ⚠️ 严格筛选：只使用明确被回收的丝球（IsAvailable=true且isActive=false）
            // 这些丝球是通过撞墙/撞玩家等显式回收的
            var availableBall = _silkBallPool.FirstOrDefault(b =>
                b != null &&
                b.IsAvailable &&           // 标记为可用
                !b.isActive            // 确认未激活
            );

            if (availableBall != null)
            {
                return availableBall;
            }

            // 没有可用的，创建新实例

            // 调试：输出池中丝球的状态统计
            int activeCount = _silkBallPool.Count(b => b != null && b.isActive);
            int availableCount = _silkBallPool.Count(b => b != null && b.IsAvailable);
            int enabledCount = _silkBallPool.Count(b => b != null && b.gameObject.activeSelf);
            Log.Info($"  池统计 - 总数:{_silkBallPool.Count}, 激活:{activeCount}, 可用:{availableCount}, GameObject启用:{enabledCount}");

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

            // 克隆预制体（作为池容器的子对象）
            var silkBallInstance = Object.Instantiate(_customSilkBallPrefab, _poolContainer.transform);
            silkBallInstance.name = $"Silk Ball #{_silkBallPool.Count}";

            // 添加 SilkBallBehavior 组件（预制体上没有，需要在实例化时添加）
            var behavior = silkBallInstance.AddComponent<SilkBallBehavior>();
            if (behavior == null)
            {
                Log.Error("无法添加 SilkBallBehavior 组件！");
                Object.Destroy(silkBallInstance);
                return null;
            }

            // 初始化 Behavior（只初始化一次，传入管理器引用）
            behavior.InitializeOnce(_poolContainer.transform, this);

            // 加入池中
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
        public SilkBallBehavior? SpawnSilkBall(Vector3 position, float acceleration, float maxSpeed, float chaseTime, float scale, bool enableRotation = true, Transform? customTarget = null, bool ignoreWall = false)
        {
            var behavior = GetAvailableSilkBall();
            if (behavior == null)
            {
                Log.Error("无法获取可用丝球，生成失败");
                return null;
            }

            // 准备丝球
            behavior.PrepareForUse(position, acceleration, maxSpeed, chaseTime, scale, enableRotation, customTarget, ignoreWall);
            return behavior;
        }

        #region Memory SilkBall Pool Management
        /// <summary>
        /// 从 Memory 对象池获取可用丝球（如果没有则创建新实例）
        /// </summary>
        private MemorySilkBallBehavior? GetAvailableMemorySilkBall()
        {
            // 严格筛选：只使用明确被回收的丝球（IsAvailable=true且isActive=false）
            var availableBall = _memorySilkBallPool.FirstOrDefault(b =>
                b != null &&
                b.IsAvailable &&           // 标记为可用
                !b.isActive            // 确认未激活
            );

            if (availableBall != null)
            {
                return availableBall;
            }

            // 没有可用的，创建新实例
            int activeCount = _memorySilkBallPool.Count(b => b != null && b.isActive);
            int availableCount = _memorySilkBallPool.Count(b => b != null && b.IsAvailable);
            int enabledCount = _memorySilkBallPool.Count(b => b != null && b.gameObject.activeSelf);
            Log.Info($"  Memory池统计 - 总数:{_memorySilkBallPool.Count}, 激活:{activeCount}, 可用:{availableCount}, GameObject启用:{enabledCount}");

            return CreateNewMemorySilkBallInstance();
        }

        /// <summary>
        /// 创建新的 Memory 丝球实例并加入池中
        /// </summary>
        private MemorySilkBallBehavior? CreateNewMemorySilkBallInstance()
        {
            if (_customSilkBallPrefab == null)
            {
                Log.Error("自定义丝球预制体未初始化，无法创建 Memory 实例");
                return null;
            }

            if (_memoryPoolContainer == null)
            {
                Log.Error("Memory 对象池容器未初始化，无法创建实例");
                return null;
            }

            // 克隆预制体（作为 Memory 池容器的子对象）
            var silkBallInstance = Object.Instantiate(_customSilkBallPrefab, _memoryPoolContainer.transform);
            silkBallInstance.name = $"Memory Silk Ball #{_memorySilkBallPool.Count}";

            // 添加 Memory 版本的 Behavior（预制体上没有任何 Behavior，直接添加）
            var behavior = silkBallInstance.AddComponent<MemorySilkBallBehavior>();
            if (behavior == null)
            {
                Log.Error("无法添加 MemorySilkBallBehavior 组件！");
                Object.Destroy(silkBallInstance);
                return null;
            }

            // 初始化 Behavior（只初始化一次，传入管理器引用）
            behavior.InitializeOnce(_memoryPoolContainer.transform, this);

            // 加入池中
            _memorySilkBallPool.Add(behavior);
            return behavior;
        }

        /// <summary>
        /// 生成并准备 Memory 丝球（基础版本）
        /// </summary>
        public MemorySilkBallBehavior? SpawnMemorySilkBall(Vector3 position)
        {
            return SpawnMemorySilkBall(position, 30f, 20f, 6f, 1f);
        }

        /// <summary>
        /// 生成并准备 Memory 丝球（完整参数版本）
        /// </summary>
        public MemorySilkBallBehavior? SpawnMemorySilkBall(Vector3 position, float acceleration, float maxSpeed, float chaseTime, float scale, bool enableRotation = true, Transform? customTarget = null, bool ignoreWall = false)
        {
            var behavior = GetAvailableMemorySilkBall();
            if (behavior == null)
            {
                Log.Error("无法获取可用 Memory 丝球，生成失败");
                return null;
            }

            // 准备丝球
            behavior.PrepareForUse(position, acceleration, maxSpeed, chaseTime, scale, enableRotation, customTarget, ignoreWall);
            return behavior;
        }

        /// <summary>
        /// 回收所有活跃的 Memory 丝球到对象池
        /// </summary>
        public void RecycleAllActiveMemorySilkBalls()
        {
            int recycledCount = 0;

            foreach (var behavior in _memorySilkBallPool)
            {
                if (behavior != null && behavior.isActive)
                {
                    behavior.RecycleToPool();
                    recycledCount++;
                }
            }

            Log.Info($"已回收所有活跃 Memory 丝球到对象池，共 {recycledCount} 个");
        }
        #endregion

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

            Log.Info("=== 丝球对象池状态 ===");
            Log.Info($"  总数: {_silkBallPool.Count}");
            Log.Info($"  可用: {available}");
            Log.Info($"  活跃中: {active}");
        }

        /// <summary>
        /// 自动补充对象池机制（普通版本）
        /// 如果池内数量小于 NORMAL_MIN_POOL_SIZE，则每 POOL_GENERATION_INTERVAL 秒生成一个到池子里
        /// </summary>
        private IEnumerator AutoPoolGeneration()
        {
            while (true)
            {
                // 等待间隔时间
                yield return new WaitForSeconds(POOL_GENERATION_INTERVAL);

                // 如果未启用普通池自动补充，跳过本次检查
                if (!_enableNormalAutoPooling)
                {
                    continue;
                }
                // 如果未初始化完成，跳过本次检查
                if (!_initialized || _customSilkBallPrefab == null || _poolContainer == null)
                {
                    continue;
                }
                // 统计池中实际存在的对象数量（排除 null）
                int currentPoolSize = _silkBallPool.Count(b => b != null);

                // 如果池内数量小于最小值，生成一个新实例到池中
                if (currentPoolSize < NORMAL_MIN_POOL_SIZE)
                {
                    var newBall = CreateNewSilkBallInstance();
                    if (newBall != null)
                    {
                        // 创建后立即回收，使其处于可用状态
                        newBall.RecycleToPool();
                    }
                }
            }
        }

        /// <summary>
        /// 自动补充 Memory 对象池机制
        /// 如果池内数量小于 MEMORY_MIN_POOL_SIZE，则每 POOL_GENERATION_INTERVAL 秒生成一个到池子里
        /// </summary>
        private IEnumerator AutoMemoryPoolGeneration()
        {
            while (true)
            {
                // 等待间隔时间
                yield return new WaitForSeconds(POOL_GENERATION_INTERVAL);

                // 如果未启用梦境池自动补充，跳过本次检查
                if (!_enableMemoryAutoPooling)
                {
                    continue;
                }
                // 如果未初始化完成，跳过本次检查
                if (!_initialized || _customSilkBallPrefab == null || _memoryPoolContainer == null)
                {
                    continue;
                }
                // 统计 Memory 池中实际存在的对象数量（排除 null）
                int currentPoolSize = _memorySilkBallPool.Count(b => b != null);

                // 如果池内数量小于最小值，生成一个新实例到池中
                if (currentPoolSize < MEMORY_MIN_POOL_SIZE)
                {
                    var newBall = CreateNewMemorySilkBallInstance();
                    if (newBall != null)
                    {
                        // 创建后立即回收，使其处于可用状态
                        newBall.RecycleToPool();
                    }
                }
            }
        }

        #region 池子加载/销毁管理（公开方法）

        /// <summary>
        /// 加载普通池子（并销毁梦境池子，保证两个池子不共存）
        /// </summary>
        public void LoadNormalPool()
        {
            if (_normalPoolLoaded)
            {
                Log.Info("[SilkBallManager] 普通池子已加载，跳过");
                return;
            }

            // 先销毁梦境池子
            if (_memoryPoolLoaded)
            {
                DestroyMemoryPool();
            }

            Log.Info("[SilkBallManager] 开始加载普通池子...");

            // 创建普通池容器
            _poolContainer = new GameObject("SilkBall Pool");
            _poolContainer.transform.SetParent(transform);

            // 启用普通池自动补充
            _enableNormalAutoPooling = true;
            _normalPoolLoaded = true;

            Log.Info($"[SilkBallManager] 普通池子已加载，目标大小: {NORMAL_MIN_POOL_SIZE}");
        }

        /// <summary>
        /// 销毁普通池子
        /// </summary>
        public void DestroyNormalPool()
        {
            if (!_normalPoolLoaded)
            {
                Log.Info("[SilkBallManager] 普通池子未加载，跳过销毁");
                return;
            }

            Log.Info("[SilkBallManager] 开始销毁普通池子...");

            // 停止自动补充
            _enableNormalAutoPooling = false;

            // 回收所有活跃丝球
            RecycleAllActiveSilkBalls();

            // 清空池列表
            _silkBallPool.Clear();

            // 销毁容器
            if (_poolContainer != null)
            {
                UnityEngine.Object.Destroy(_poolContainer);
                _poolContainer = null;
            }

            _normalPoolLoaded = false;
            Log.Info("[SilkBallManager] 普通池子已销毁");
        }

        /// <summary>
        /// 加载梦境池子（并销毁普通池子，保证两个池子不共存）
        /// </summary>
        public void LoadMemoryPool()
        {
            if (_memoryPoolLoaded)
            {
                Log.Info("[SilkBallManager] 梦境池子已加载，跳过");
                return;
            }

            // 先销毁普通池子
            if (_normalPoolLoaded)
            {
                DestroyNormalPool();
            }

            Log.Info("[SilkBallManager] 开始加载梦境池子...");

            // 创建梦境池容器
            _memoryPoolContainer = new GameObject("Memory SilkBall Pool");
            _memoryPoolContainer.transform.SetParent(transform);

            // 启用梦境池自动补充
            _enableMemoryAutoPooling = true;
            _memoryPoolLoaded = true;

            Log.Info($"[SilkBallManager] 梦境池子已加载，目标大小: {MEMORY_MIN_POOL_SIZE}");
        }

        /// <summary>
        /// 销毁梦境池子
        /// </summary>
        public void DestroyMemoryPool()
        {
            if (!_memoryPoolLoaded)
            {
                Log.Info("[SilkBallManager] 梦境池子未加载，跳过销毁");
                return;
            }

            Log.Info("[SilkBallManager] 开始销毁梦境池子...");

            // 停止自动补充
            _enableMemoryAutoPooling = false;

            // 回收所有活跃丝球
            RecycleAllActiveMemorySilkBalls();

            // 清空池列表
            _memorySilkBallPool.Clear();

            // 销毁容器
            if (_memoryPoolContainer != null)
            {
                UnityEngine.Object.Destroy(_memoryPoolContainer);
                _memoryPoolContainer = null;
            }

            _memoryPoolLoaded = false;
            Log.Info("[SilkBallManager] 梦境池子已销毁");
        }

        /// <summary>
        /// 检查普通池子是否已加载
        /// </summary>
        public bool IsNormalPoolLoaded => _normalPoolLoaded;

        /// <summary>
        /// 检查梦境池子是否已加载
        /// </summary>
        public bool IsMemoryPoolLoaded => _memoryPoolLoaded;

        #endregion

        /// <summary>
        /// 场景切换或销毁时的完全清理（更彻底）
        /// </summary>
        private void CleanupAllSilkBallsOnDestroy()
        {
            Log.Info("SilkBallManager场景切换/销毁，执行完全清理");

            // 停止所有协程
            StopAllCoroutines();

            // 停止自动补充
            _enableNormalAutoPooling = false;
            _enableMemoryAutoPooling = false;

            // 回收所有活跃丝球（普通版本）
            RecycleAllActiveSilkBalls();

            // 回收所有活跃丝球（Memory 版本）
            RecycleAllActiveMemorySilkBalls();

            // 清空普通池列表
            if (_silkBallPool != null)
            {
                _silkBallPool.Clear();
                Log.Info("已清空丝球对象池");
            }

            // 清空 Memory 池列表
            if (_memorySilkBallPool != null)
            {
                _memorySilkBallPool.Clear();
                Log.Info("已清空 Memory 丝球对象池");
            }

            // 销毁普通池容器
            if (_poolContainer != null)
            {
                UnityEngine.Object.Destroy(_poolContainer);
                _poolContainer = null;
                Log.Info("已销毁丝球对象池容器");
            }

            // 销毁 Memory 池容器
            if (_memoryPoolContainer != null)
            {
                UnityEngine.Object.Destroy(_memoryPoolContainer);
                _memoryPoolContainer = null;
                Log.Info("已销毁 Memory 丝球对象池容器");
            }

            // 重置加载状态
            _normalPoolLoaded = false;
            _memoryPoolLoaded = false;
            _initialized = false;
        }
    }
}
