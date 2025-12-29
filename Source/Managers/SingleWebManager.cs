using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours.Normal;
using AnySilkBoss.Source.Behaviours.Memory;
using HutongGames.PlayMaker.Actions;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 单根丝线管理器
    /// 负责管理和控制单根丝线攻击（带对象池和冷却系统）
    /// </summary>
    internal class SingleWebManager : MonoBehaviour
    {
        #region Fields
        // 原版丝线资源
        private GameObject? _strandPatterns;
        private GameObject? _pattern1Template;

        // 单根丝线预制体（模板，用于后续生成实例）
        private GameObject? _singleWebStrandPrefab;
        public GameObject? SingleWebStrandPrefab => _singleWebStrandPrefab;

        // 对象池 - 普通版本
        private readonly List<SingleWebBehavior> _webPool = new List<SingleWebBehavior>();
        private GameObject? _poolContainer;  // 池容器（用于组织层级）

        // 对象池 - Memory 版本
        private readonly List<MemorySingleWebBehavior> _memoryWebPool = new List<MemorySingleWebBehavior>();
        private GameObject? _memoryPoolContainer;  // Memory 池容器

        // 初始化标志
        private bool _initialized = false;

        // BOSS 场景名称
        private const string BossSceneName = "Cradle_03";

        // 自动补充池机制
        private bool _enableNormalAutoPooling = false;  // 普通池自动补充
        private bool _enableMemoryAutoPooling = false;  // 梦境池自动补充
        private const int NORMAL_MIN_POOL_SIZE = 20;   // 普通池最小数量
        private const int MEMORY_MIN_POOL_SIZE = 70;   // 梦境池最小数量（P6领域次元斩需要70根）
        private const float POOL_GENERATION_INTERVAL = 0.2f;  // 生成间隔

        // 池子加载状态
        private bool _normalPoolLoaded = false;
        private bool _memoryPoolLoaded = false;
        #endregion

        #region Unity Lifecycle

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
        #endregion

        #region Scene Management
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
            Log.Info($"检测到 BOSS 场景 {scene.name}，开始初始化 SingleWebManager...");
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
                Log.Info($"离开 BOSS 场景 {oldScene.name}，清理 SingleWebManager 缓存");

                CleanupPool();
                _initialized = false;
                Log.Info("=== SingleWebManager 清理完成 ===");
            }
        }

        #endregion

        #region Initialization
        /// <summary>
        /// 初始化单根丝线管理器
        /// </summary>
        private IEnumerator Initialize()
        {
            Log.Info("=== 开始初始化 SingleWebManager ===");

            // 每次进入场景都需要重新创建预制体，因为组件引用会随场景切换失效
            // 先销毁旧的预制体
            if (_singleWebStrandPrefab != null)
            {
                Log.Info("销毁旧的丝线预制体...");
                Object.Destroy(_singleWebStrandPrefab);
                _singleWebStrandPrefab = null;
            }

            // 等待场景加载完成
            yield return new WaitForSeconds(0.5f);

            // 获取原版丝线资源
            GetStrandPatterns();

            // 获取 Pattern 1 模板
            GetPattern1Template();

            // 创建单根丝线预制体
            yield return CreateSingleWebStrandPrefab();

            _initialized = true;
            Log.Info("=== SingleWebManager 初始化完成 ===");

            // 启动自动补充池机制（会根据各自的启用标记决定是否工作）
            StartCoroutine(AutoPoolGeneration());
            StartCoroutine(AutoMemoryPoolGeneration());

            // 根据 MemoryManager 判断加载哪个池子
            if (MemoryManager.IsInMemoryMode)
            {
                Log.Info("[SingleWebManager] 检测到 Memory 模式，加载梦境池子");
                LoadMemoryPool();
            }
            else
            {
                Log.Info("[SingleWebManager] 普通模式，加载普通池子");
                LoadNormalPool();
            }
        }

        /// <summary>
        /// 获取原版 _strandPatterns
        /// </summary>
        private void GetStrandPatterns()
        {
            // 方式1：从场景中查找（假设在 Boss Scene 下）
            var bossScene = GameObject.Find("Boss Scene");
            if (bossScene != null)
            {
                var strandPatternsTransform = bossScene.transform.Find("Strand Patterns");
                if (strandPatternsTransform != null)
                {
                    _strandPatterns = strandPatternsTransform.gameObject;
                    Log.Info($"从 Boss Scene 找到 Strand Patterns: {_strandPatterns.name}");
                    return;
                }
            }
            Log.Error("未找到 Strand Patterns GameObject！");
        }

        /// <summary>
        /// 获取 Pattern 1 作为模板
        /// </summary>
        private void GetPattern1Template()
        {
            if (_strandPatterns == null)
            {
                Log.Error("Strand Patterns 为 null，无法获取 Pattern 1");
                return;
            }

            var pattern1Transform = _strandPatterns.transform.Find("Pattern 1");
            if (pattern1Transform != null)
            {
                _pattern1Template = pattern1Transform.gameObject;
                Log.Info($"找到 Pattern 1 模板: {_pattern1Template.name}");
            }
            else
            {
                Log.Error("未找到 Pattern 1 GameObject！");
            }
        }

        /// <summary>
        /// 创建单根丝线预制体（从 Pattern 1 的第一个 WebStrand 克隆）
        /// </summary>
        private IEnumerator CreateSingleWebStrandPrefab()
        {
            Log.Info("=== 开始创建单根丝线预制体 ===");

            if (_pattern1Template == null)
            {
                Log.Error("Pattern 1 模板为 null，无法创建单根丝线预制体");
                yield break;
            }

            // 找到第一个 Silk Boss WebStrand
            GameObject? firstWebStrand = null;
            foreach (Transform child in _pattern1Template.transform)
            {
                if (child.name.Contains("Silk Boss WebStrand"))
                {
                    firstWebStrand = child.gameObject;
                    break;
                }
            }

            if (firstWebStrand == null)
            {
                Log.Error("未找到 Silk Boss WebStrand，无法创建单根丝线预制体");
                yield break;
            }

            Log.Info($"找到第一个 WebStrand: {firstWebStrand.name}");

            // 克隆 WebStrand 作为预制体
            _singleWebStrandPrefab = Object.Instantiate(firstWebStrand);
            _singleWebStrandPrefab.name = "Single WebStrand Prefab";
            _singleWebStrandPrefab.transform.SetScaleX(5f);  // 2.5倍长度（原版2f × 2.5 = 5f）

            // 不需要 DontDestroyOnLoad，因为 SingleWebManager 已经在 AnySilkBossManager 上，后者已设置 DontDestroyOnLoad
            // 预制体作为 Manager 的子物体保存
            _singleWebStrandPrefab.transform.SetParent(transform);

            // 立即禁用（作为预制体模板）
            _singleWebStrandPrefab.SetActive(false);

            // 配置预制体：添加 DamageHero 和基础组件
            ConfigureWebStrandPrefab();

            Log.Info($"=== 单根丝线预制体创建完成: {_singleWebStrandPrefab.name} ===");
            yield return null;
        }

        #endregion

        #region 池子加载/销毁管理（公开方法）

        /// <summary>
        /// 加载普通池子（并销毁梦境池子，保证两个池子不共存）
        /// </summary>
        public void LoadNormalPool()
        {
            if (!_initialized)
            {
                Log.Warn("[SingleWebManager] 尚未初始化，无法加载普通池子");
                return;
            }

            if (_normalPoolLoaded)
            {
                Log.Info("[SingleWebManager] 普通池子已加载，跳过");
                return;
            }

            // 先销毁梦境池子
            if (_memoryPoolLoaded)
            {
                DestroyMemoryPool();
            }

            Log.Info("[SingleWebManager] 开始加载普通池子...");

            // 创建普通池容器
            _poolContainer = new GameObject("SingleWeb Pool");
            _poolContainer.transform.SetParent(transform);

            // 启用普通池自动补充
            _enableNormalAutoPooling = true;
            _normalPoolLoaded = true;

            Log.Info($"[SingleWebManager] 普通池子已加载，目标大小: {NORMAL_MIN_POOL_SIZE}");
        }

        /// <summary>
        /// 销毁普通池子
        /// </summary>
        public void DestroyNormalPool()
        {
            if (!_normalPoolLoaded)
            {
                Log.Info("[SingleWebManager] 普通池子未加载，跳过销毁");
                return;
            }

            Log.Info("[SingleWebManager] 开始销毁普通池子...");

            // 停止自动补充
            _enableNormalAutoPooling = false;

            // 销毁所有实例
            if (_webPool != null)
            {
                int destroyedCount = 0;
                foreach (var web in _webPool)
                {
                    if (web != null && web.gameObject != null)
                    {
                        Object.Destroy(web.gameObject);
                        destroyedCount++;
                    }
                }
                _webPool.Clear();
                Log.Info($"已销毁普通对象池中的 {destroyedCount} 个丝线实例");
            }

            // 销毁容器
            if (_poolContainer != null)
            {
                Object.Destroy(_poolContainer);
                _poolContainer = null;
            }

            _normalPoolLoaded = false;
            Log.Info("[SingleWebManager] 普通池子已销毁");
        }

        /// <summary>
        /// 加载梦境池子（并销毁普通池子，保证两个池子不共存）
        /// </summary>
        public void LoadMemoryPool()
        {
            if (!_initialized)
            {
                Log.Warn("[SingleWebManager] 尚未初始化，无法加载梦境池子");
                return;
            }

            if (_memoryPoolLoaded)
            {
                Log.Info("[SingleWebManager] 梦境池子已加载，跳过");
                return;
            }

            // 先销毁普通池子
            if (_normalPoolLoaded)
            {
                DestroyNormalPool();
            }

            Log.Info("[SingleWebManager] 开始加载梦境池子...");

            // 创建梦境池容器
            _memoryPoolContainer = new GameObject("Memory SingleWeb Pool");
            _memoryPoolContainer.transform.SetParent(transform);

            // 启用梦境池自动补充
            _enableMemoryAutoPooling = true;
            _memoryPoolLoaded = true;

            Log.Info($"[SingleWebManager] 梦境池子已加载，目标大小: {MEMORY_MIN_POOL_SIZE}");
        }

        /// <summary>
        /// 销毁梦境池子
        /// </summary>
        public void DestroyMemoryPool()
        {
            if (!_memoryPoolLoaded)
            {
                Log.Info("[SingleWebManager] 梦境池子未加载，跳过销毁");
                return;
            }

            Log.Info("[SingleWebManager] 开始销毁梦境池子...");

            // 停止自动补充
            _enableMemoryAutoPooling = false;

            // 销毁所有实例
            if (_memoryWebPool != null)
            {
                int destroyedCount = 0;
                foreach (var web in _memoryWebPool)
                {
                    if (web != null && web.gameObject != null)
                    {
                        Object.Destroy(web.gameObject);
                        destroyedCount++;
                    }
                }
                _memoryWebPool.Clear();
                Log.Info($"已销毁 Memory 对象池中的 {destroyedCount} 个丝线实例");
            }

            // 销毁容器
            if (_memoryPoolContainer != null)
            {
                Object.Destroy(_memoryPoolContainer);
                _memoryPoolContainer = null;
            }

            _memoryPoolLoaded = false;
            Log.Info("[SingleWebManager] 梦境池子已销毁");
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

        #region Object Pool
        /// <summary>
        /// 从对象池获取可用丝线（如果没有则创建新实例）
        /// </summary>
        /// <returns>可用的丝线 Behavior</returns>
        private SingleWebBehavior? GetAvailableWeb()
        {
            // 先查找池中可用的丝线
            var availableWeb = _webPool.FirstOrDefault(w => w != null && w.IsAvailable);
            
            if (availableWeb != null)
            {
                Log.Debug($"从池中获取可用丝线: {availableWeb.gameObject.name}");
                return availableWeb;
            }

            // 没有可用的，创建新实例
            Log.Info("池中无可用丝线，创建新实例...");
            return CreateNewWebInstance();
        }

        /// <summary>
        /// 创建新的丝线实例并加入池中
        /// </summary>
        private SingleWebBehavior? CreateNewWebInstance()
        {
            if (_singleWebStrandPrefab == null)
            {
                Log.Error("单根丝线预制体未初始化，无法创建实例");
                return null;
            }

            if (_poolContainer == null)
            {
                Log.Error("对象池容器未初始化，无法创建实例");
                return null;
            }

            // 克隆预制体（作为池容器的子对象）
            var webInstance = Object.Instantiate(_singleWebStrandPrefab, _poolContainer.transform);
            webInstance.name = $"Single WebStrand #{_webPool.Count}";
            webInstance.SetActive(true);
            // 配置基础组件
            ConfigureWebInstance(webInstance);

            // 添加并初始化 Behavior（传入池容器引用）
            var behavior = webInstance.AddComponent<SingleWebBehavior>();
            behavior.InitializeBehavior(_poolContainer.transform);
            // 加入池中
            _webPool.Add(behavior);
            return behavior;
        }
        #endregion

        #region Memory Object Pool
        /// <summary>
        /// 从 Memory 对象池获取可用丝线（如果没有则创建新实例）
        /// </summary>
        private MemorySingleWebBehavior? GetAvailableMemoryWeb()
        {
            // 先查找池中可用的丝线
            var availableWeb = _memoryWebPool.FirstOrDefault(w => w != null && w.IsAvailable);
            
            if (availableWeb != null)
            {
                Log.Debug($"从 Memory 池中获取可用丝线: {availableWeb.gameObject.name}");
                return availableWeb;
            }

            // 没有可用的，创建新实例
            Log.Info("Memory 池中无可用丝线，创建新实例...");
            return CreateNewMemoryWebInstance();
        }

        /// <summary>
        /// 创建新的 Memory 丝线实例并加入池中
        /// </summary>
        private MemorySingleWebBehavior? CreateNewMemoryWebInstance()
        {
            if (_singleWebStrandPrefab == null)
            {
                Log.Error("单根丝线预制体未初始化，无法创建 Memory 实例");
                return null;
            }

            if (_memoryPoolContainer == null)
            {
                Log.Error("Memory 对象池容器未初始化，无法创建实例");
                return null;
            }

            // 克隆预制体（作为 Memory 池容器的子对象）
            var webInstance = Object.Instantiate(_singleWebStrandPrefab, _memoryPoolContainer.transform);
            webInstance.name = $"Memory SingleWeb #{_memoryWebPool.Count}";
            webInstance.SetActive(true);

            // 配置基础组件
            ConfigureWebInstance(webInstance);

            // 移除普通版本的 Behavior（如果有）
            var normalBehavior = webInstance.GetComponent<SingleWebBehavior>();
            if (normalBehavior != null)
            {
                Object.Destroy(normalBehavior);
            }

            // 添加并初始化 Memory Behavior
            var behavior = webInstance.AddComponent<MemorySingleWebBehavior>();
            behavior.InitializeBehavior(_memoryPoolContainer.transform);

            // 加入池中
            _memoryWebPool.Add(behavior);
            return behavior;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 生成单根丝线并触发攻击（使用对象池）
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <param name="rotation">旋转角度（欧拉角，默认为 null）</param>
        /// <param name="scale">缩放比例（默认为 null = Vector3.one）</param>
        /// <param name="appearDelay">出现延迟（秒，默认 0）</param>
        /// <param name="burstDelay">爆发延迟（秒，默认 0.75）</param>
        /// <returns>生成的丝线 Behavior（null 表示失败）</returns>
        public SingleWebBehavior? SpawnAndAttack(
            Vector3 position,
            Vector3? rotation = null,
            Vector3? scale = null,
            float appearDelay = 0f,
            float burstDelay = 0.75f)
        {
            // 从池中获取可用丝线
            var webBehavior = GetAvailableWeb();
            if (webBehavior == null)
            {
                Log.Error("无法获取可用丝线，生成失败");
                return null;
            }

            // 设置位置和变换
            webBehavior.transform.position = position;
            webBehavior.transform.eulerAngles = rotation ?? Vector3.zero;
            webBehavior.transform.localScale = scale ?? Vector3.one;

            // 触发攻击
            webBehavior.TriggerAttack(appearDelay, burstDelay);
            return webBehavior;
        }

        /// <summary>
        /// 简化版：只指定位置
        /// </summary>
        public SingleWebBehavior? SpawnAndAttack(Vector3 position)
        {
            return SpawnAndAttack(position, null, null, 0f, 0.75f);
        }

        #region Memory Public Methods
        /// <summary>
        /// 生成 Memory 单根丝线并触发攻击（使用对象池）
        /// </summary>
        public MemorySingleWebBehavior? SpawnMemoryWebAndAttack(
            Vector3 position,
            Vector3? rotation = null,
            Vector3? scale = null,
            float appearDelay = 0f,
            float burstDelay = 0.75f)
        {
            // 从 Memory 池中获取可用丝线
            var webBehavior = GetAvailableMemoryWeb();
            if (webBehavior == null)
            {
                Log.Error("无法获取可用 Memory 丝线，生成失败");
                return null;
            }

            // 设置位置和变换
            webBehavior.transform.position = position;
            webBehavior.transform.eulerAngles = rotation ?? Vector3.zero;
            webBehavior.transform.localScale = scale ?? Vector3.one;

            // 触发攻击
            webBehavior.TriggerAttack(appearDelay, burstDelay);

            Log.Debug($"已生成并触发 Memory 丝线攻击: {webBehavior.gameObject.name} at {position}");
            return webBehavior;
        }

        /// <summary>
        /// Memory 简化版：只指定位置
        /// </summary>
        public MemorySingleWebBehavior? SpawnMemoryWebAndAttack(Vector3 position)
        {
            return SpawnMemoryWebAndAttack(position, null, null, 0f, 0.75f);
        }

        /// <summary>
        /// 批量生成多根 Memory 丝线并触发攻击
        /// </summary>
        public List<MemorySingleWebBehavior> SpawnMultipleMemoryWebAndAttack(
            Vector3[] positions,
            Vector3? rotation = null,
            Vector3? scale = null,
            Vector2? randomAppearDelay = null,
            float burstDelay = 0.75f)
        {
            List<MemorySingleWebBehavior> behaviors = new List<MemorySingleWebBehavior>();
            Vector2 delayRange = randomAppearDelay ?? new Vector2(0f, 0.3f);

            foreach (var pos in positions)
            {
                float randomDelay = Random.Range(delayRange.x, delayRange.y);
                var behavior = SpawnMemoryWebAndAttack(pos, rotation, scale, randomDelay, burstDelay);
                
                if (behavior != null)
                {
                    behaviors.Add(behavior);
                }
            }

            Log.Info($"批量生成了 {behaviors.Count} 根 Memory 丝线并触发攻击");
            return behaviors;
        }

        /// <summary>
        /// 清空 Memory 对象池
        /// </summary>
        public void ClearMemoryPool()
        {
            foreach (var web in _memoryWebPool)
            {
                if (web != null)
                {
                    web.StopAttack();
                    web.ResetCooldown();
                }
            }
            Log.Info($"已清空 Memory 对象池（共 {_memoryWebPool.Count} 个丝线）");
        }

        /// <summary>
        /// 确保 Memory 对象池有足够的容量
        /// </summary>
        /// <param name="minCount">最小数量</param>
        public void EnsureMemoryPoolCapacity(int minCount)
        {
            if (!_initialized || _singleWebStrandPrefab == null || _memoryPoolContainer == null)
            {
                Log.Warn("SingleWebManager 未初始化，无法确保池容量");
                return;
            }

            int currentCount = _memoryWebPool.Count(w => w != null);
            int needed = minCount - currentCount;

            if (needed > 0)
            {
                Log.Info($"Memory 池当前有 {currentCount} 根丝线，需要 {needed} 根，开始预热...");
                for (int i = 0; i < needed; i++)
                {
                    var web = CreateNewMemoryWebInstance();
                    if (web != null)
                    {
                        web.ResetCooldown();
                    }
                }
                Log.Info($"Memory 池预热完成，当前有 {_memoryWebPool.Count(w => w != null)} 根丝线");
            }
        }
        #endregion

        #region Auto Pool Generation
        /// <summary>
        /// 自动补充普通对象池机制
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
                if (!_initialized || _singleWebStrandPrefab == null || _poolContainer == null)
                {
                    continue;
                }
                // 统计池中实际存在的对象数量（排除 null）
                int currentPoolSize = _webPool.Count(w => w != null);

                // 如果池内数量小于最小值，生成一个新实例到池中
                if (currentPoolSize < NORMAL_MIN_POOL_SIZE)
                {
                    var newWeb = CreateNewWebInstance();
                    if (newWeb != null)
                    {
                        // 创建后立即重置冷却，使其处于可用状态
                        newWeb.ResetCooldown();
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
                if (!_initialized || _singleWebStrandPrefab == null || _memoryPoolContainer == null)
                {
                    continue;
                }
                // 统计 Memory 池中实际存在的对象数量（排除 null）
                int currentPoolSize = _memoryWebPool.Count(w => w != null);

                // 如果池内数量小于最小值，生成一个新实例到池中
                if (currentPoolSize < MEMORY_MIN_POOL_SIZE)
                {
                    var newWeb = CreateNewMemoryWebInstance();
                    if (newWeb != null)
                    {
                        // 创建后立即重置冷却，使其处于可用状态
                        newWeb.ResetCooldown();
                    }
                }
            }
        }
        #endregion

        /// <summary>
        /// 配置丝线实例的基础组件（Rigidbody2D、Layer、Collider 等）
        /// </summary>
        private void ConfigureWebInstance(GameObject webInstance)
        {
            Log.Debug($"配置丝线实例基础组件: {webInstance.name}");

            // 1. 配置 Rigidbody2D
            var rb2d = webInstance.GetComponent<Rigidbody2D>();
            if (rb2d == null)
            {
                rb2d = webInstance.AddComponent<Rigidbody2D>();
            }

            rb2d.bodyType = RigidbodyType2D.Dynamic;
            rb2d.gravityScale = 0f;
            rb2d.linearDamping = 0f;
            rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // 2. 设置 Layer
            webInstance.layer = LayerMask.NameToLayer("Enemy Attack");
            
            var heroCatcher = FindChildRecursive(webInstance.transform, "hero_catcher");
            if (heroCatcher != null)
            {
                heroCatcher.gameObject.layer = LayerMask.NameToLayer("Enemy Attack");
                
                // 确保 Collider 是 Trigger
                var collider = heroCatcher.GetComponent<Collider2D>();
                if (collider != null)
                {
                    collider.isTrigger = true;
                }
            }

            Log.Debug($"丝线实例基础配置完成: {webInstance.name}");
        }

        /// <summary>
        /// 批量生成多根丝线并触发攻击
        /// </summary>
        /// <param name="positions">位置数组</param>
        /// <param name="rotation">统一旋转角度</param>
        /// <param name="scale">统一缩放比例</param>
        /// <param name="randomAppearDelay">每根丝线的随机出现延迟范围（默认 0-0.3）</param>
        /// <param name="burstDelay">爆发延迟（秒）</param>
        /// <returns>生成的丝线 Behavior 列表</returns>
        public List<SingleWebBehavior> SpawnMultipleAndAttack(
            Vector3[] positions,
            Vector3? rotation = null,
            Vector3? scale = null,
            Vector2? randomAppearDelay = null,
            float burstDelay = 0.75f)
        {
            List<SingleWebBehavior> behaviors = new List<SingleWebBehavior>();
            Vector2 delayRange = randomAppearDelay ?? new Vector2(0f, 0.3f);

            foreach (var pos in positions)
            {
                float randomDelay = Random.Range(delayRange.x, delayRange.y);
                var behavior = SpawnAndAttack(pos, rotation, scale, randomDelay, burstDelay);
                
                if (behavior != null)
                {
                    behaviors.Add(behavior);
                }
            }

            Log.Info($"批量生成了 {behaviors.Count} 根丝线并触发攻击");
            return behaviors;
        }

        /// <summary>
        /// 配置单根丝线预制体（添加 DamageHero 组件）
        /// 注意：不修改 FSM，FSM 修改由 SingleWebBehavior 负责
        /// </summary>
        private void ConfigureWebStrandPrefab()
        {
            if (_singleWebStrandPrefab == null)
            {
                Log.Error("单根丝线预制体为 null，无法配置");
                return;
            }

            // 1. 找到 hero_catcher
            Transform? heroCatcherTransform = FindChildRecursive(_singleWebStrandPrefab.transform, "hero_catcher");
            if (heroCatcherTransform == null)
            {
                Log.Error("未找到 hero_catcher，无法添加 DamageHero 组件");
                return;
            }

            GameObject heroCatcher = heroCatcherTransform.gameObject;

            // 2. 检查是否已存在 DamageHero
            var existingDamageHero = heroCatcher.GetComponent<DamageHero>();
            if (existingDamageHero != null)
            {
                Log.Info("hero_catcher 已有 DamageHero 组件，先移除");
                Object.Destroy(existingDamageHero);
            }

            // 3. 添加 DamageHero 组件
            var damageHero = heroCatcher.AddComponent<DamageHero>();

            // 4. 配置 DamageHero 参数
            damageHero.damageDealt = 2;
            damageHero.hazardType = GlobalEnums.HazardType.ENEMY;
            damageHero.resetOnEnable = false;
            damageHero.collisionSide = GlobalEnums.CollisionSide.top;
            damageHero.canClashTink = false;
            damageHero.noClashFreeze = true;
            damageHero.noTerrainThunk = true;
            damageHero.noTerrainRecoil = true;
            damageHero.hasNonBouncer = false;
            damageHero.overrideCollisionSide = false;
            damageHero.enabled = false;

            // 5. 从 DamageHeroEventManager 获取并设置 OnDamagedHero 事件
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                var damageHeroEventManager = managerObj.GetComponent<DamageHeroEventManager>();
                if (damageHeroEventManager != null && damageHeroEventManager.HasDamageHero())
                {
                    var originalDamageHero = damageHeroEventManager.DamageHero;
                    if (originalDamageHero != null && originalDamageHero.OnDamagedHero != null)
                    {
                        damageHero.OnDamagedHero = originalDamageHero.OnDamagedHero;
                    }
                    else
                    {
                        Log.Warn("原始 DamageHero 的 OnDamagedHero 事件为 null，初始化为空事件");
                        damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
                    }
                }
                else
                {
                    Log.Warn("DamageHeroEventManager 未初始化或未找到 DamageHero，初始化为空事件");
                    damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
                }
            }
            else
            {
                Log.Warn("未找到 AnySilkBossManager，初始化为空事件");
                damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
            }

            Log.Info($"已配置预制体 DamageHero: 伤害={damageHero.damageDealt}");
            Log.Info("=== 预制体配置完成（FSM 将由 SingleWebBehavior 修改）===");
        }

        /// <summary>
        /// 清空对象池（停止所有攻击并重置冷却）
        /// </summary>
        public void ClearPool()
        {
            foreach (var web in _webPool)
            {
                if (web != null)
                {
                    web.StopAttack();
                    web.ResetCooldown();
                }
            }
            Log.Info($"已清空对象池（共 {_webPool.Count} 个丝线）");
        }

        /// <summary>
        /// 递归查找子物体
        /// </summary>
        private Transform? FindChildRecursive(Transform parent, string childName)
        {
            Transform? child = parent.Find(childName);
            if (child != null)
                return child;

            foreach (Transform t in parent)
            {
                child = FindChildRecursive(t, childName);
                if (child != null)
                    return child;
            }

            return null;
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized()
        {
            return _initialized;
        }

        /// <summary>
        /// 获取 _strandPatterns 引用（供外部使用）
        /// </summary>
        public GameObject? GetStrandPatternsReference()
        {
            return _strandPatterns;
        }

        /// <summary>
        /// 获取 Pattern 1 模板引用（供外部使用）
        /// </summary>
        public GameObject? GetPattern1TemplateReference()
        {
            return _pattern1Template;
        }

        /// <summary>
        /// 清理预制体和缓存（离开 BOSS 场景时调用）
        /// </summary>
        public void CleanupPool()
        {
            Log.Info("=== 开始清理 SingleWebManager 缓存 ===");

            // 停止所有协程
            StopAllCoroutines();
            // 停止自动补充
            _enableNormalAutoPooling = false;
            _enableMemoryAutoPooling = false;

            // 销毁普通池子（如果已加载）
            if (_normalPoolLoaded)
            {
                // 清理普通对象池：销毁所有实例
                if (_webPool != null)
                {
                    int destroyedCount = 0;
                    foreach (var web in _webPool)
                    {
                        if (web != null && web.gameObject != null)
                        {
                            Object.Destroy(web.gameObject);
                            destroyedCount++;
                        }
                    }
                    _webPool.Clear();
                    Log.Info($"已销毁普通对象池中的 {destroyedCount} 个丝线实例");
                }

                // 销毁普通对象池容器
                if (_poolContainer != null)
                {
                    Object.Destroy(_poolContainer);
                    _poolContainer = null;
                    Log.Info("已销毁普通对象池容器");
                }
                _normalPoolLoaded = false;
            }

            // 销毁梦境池子（如果已加载）
            if (_memoryPoolLoaded)
            {
                // 清理 Memory 对象池：销毁所有实例
                if (_memoryWebPool != null)
                {
                    int destroyedCount = 0;
                    foreach (var web in _memoryWebPool)
                    {
                        if (web != null && web.gameObject != null)
                        {
                            Object.Destroy(web.gameObject);
                            destroyedCount++;
                        }
                    }
                    _memoryWebPool.Clear();
                    Log.Info($"已销毁 Memory 对象池中的 {destroyedCount} 个丝线实例");
                }

                // 销毁 Memory 对象池容器
                if (_memoryPoolContainer != null)
                {
                    Object.Destroy(_memoryPoolContainer);
                    _memoryPoolContainer = null;
                    Log.Info("已销毁 Memory 对象池容器");
                }
                _memoryPoolLoaded = false;
            }

            // 销毁预制体
            if (_singleWebStrandPrefab != null)
            {
                Object.Destroy(_singleWebStrandPrefab);
                _singleWebStrandPrefab = null;
                Log.Info("已销毁单根丝线预制体");
            }

            // 清理场景引用
            _strandPatterns = null;
            _pattern1Template = null;

            // 重置初始化标志
            _initialized = false;

            Log.Info("=== SingleWebManager 清理完成 ===");
        }
        #endregion
    }
}

