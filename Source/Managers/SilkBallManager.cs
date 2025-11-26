using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours;

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

        // 对象池
        private readonly List<SilkBallBehavior> _silkBallPool = new List<SilkBallBehavior>();
        private GameObject? _poolContainer;

        // 自动补充池机制（默认不启用）
        private bool _enableAutoPooling = true;
        private const int MIN_POOL_SIZE = 120;  // 从80扩大到120，预留大丝球爆炸产生的约80个丝球
        private const float POOL_GENERATION_INTERVAL = 0.1f;
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

            // 启动自动补充池机制（默认不启用，可通过设置 _enableAutoPooling = true 来启用）
            StartCoroutine(AutoPoolGeneration());
        }

        /// <summary>
        /// 创建对象池容器
        /// </summary>
        private void CreatePoolContainer()
        {
            _poolContainer = new GameObject("SilkBall Pool");
            _poolContainer.transform.SetParent(transform);
            // DontDestroyOnLoad(_poolContainer);
            Log.Info("已创建丝球对象池容器（不随场景销毁）");
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

            // 永久保存，不随场景销毁
            DontDestroyOnLoad(_customSilkBallPrefab);

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

            // 添加 SilkBallBehavior 组件
            var silkBallBehavior = _customSilkBallPrefab.AddComponent<SilkBallBehavior>();
            if (silkBallBehavior != null)
            {
                Log.Info("成功添加 SilkBallBehavior 组件");
            }

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

            // 获取 Behavior 组件
            var behavior = silkBallInstance.GetComponent<SilkBallBehavior>();
            if (behavior == null)
            {
                Log.Error("丝球实例没有 SilkBallBehavior 组件！");
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
        /// 自动补充对象池机制
        /// 如果池内数量小于 MIN_POOL_SIZE，则每 POOL_GENERATION_INTERVAL 秒生成一个到池子里
        /// </summary>
        private IEnumerator AutoPoolGeneration()
        {
            while (true)
            {
                // 等待间隔时间
                yield return new WaitForSeconds(POOL_GENERATION_INTERVAL);

                // 如果未启用，跳过本次检查
                if (!_enableAutoPooling)
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
                if (currentPoolSize < MIN_POOL_SIZE)
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
        /// 场景切换或销毁时的完全清理（更彻底）
        /// </summary>
        private void CleanupAllSilkBallsOnDestroy()
        {
            Log.Info("SilkBallManager场景切换/销毁，执行完全清理");

            // 停止所有协程
            StopAllCoroutines();

            // 回收所有活跃丝球
            RecycleAllActiveSilkBalls();

            // 清空池列表
            if (_silkBallPool != null)
            {
                _silkBallPool.Clear();
                Log.Info("已清空丝球对象池");
            }

            // 销毁池容器
            if (_poolContainer != null)
            {
                UnityEngine.Object.Destroy(_poolContainer);
                _poolContainer = null;
                Log.Info("已销毁丝球对象池容器");
            }

            _initialized = false;
        }
    }
}
