using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours;
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

        // 对象池
        private readonly List<SingleWebBehavior> _webPool = new List<SingleWebBehavior>();
        private GameObject? _poolContainer;  // 池容器（用于组织层级）

        // 初始化标志
        private bool _initialized = false;

        // BOSS 场景名称
        private const string BossSceneName = "Cradle_03";
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

        private void Update()
        {
            // 按 T 键重新分析 FSM（调试用）
            if (Input.GetKeyDown(KeyCode.T) && _initialized)
            {
                Log.Info("手动触发 FSM 分析");
                AnalyzeOriginalFSMs();
            }

            // 按 Y 键测试单根丝线生成和攻击（调试用）
            if (Input.GetKeyDown(KeyCode.Y) && _initialized)
            {
                Log.Info("手动触发单根丝线测试");
                TestSingleWebStrand();
            }
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
            // 当离开 BOSS 场景时清理
            if (oldScene.name == BossSceneName)
            {
                Log.Info($"离开 BOSS 场景 {oldScene.name}，清理 SingleWebManager 缓存");
                CleanupPrefab();
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

            // 等待场景加载完成
            yield return new WaitForSeconds(0.5f);

            // 获取原版丝线资源
            GetStrandPatterns();

            // 获取 Pattern 1 模板
            GetPattern1Template();

            // 创建单根丝线预制体
            yield return CreateSingleWebStrandPrefab();

            // 创建对象池容器
            CreatePoolContainer();

            _initialized = true;
            Log.Info("=== SingleWebManager 初始化完成 ===");
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
                LogPattern1Structure();
            }
            else
            {
                Log.Error("未找到 Pattern 1 GameObject！");
            }
        }

        /// <summary>
        /// 记录 Pattern 1 的结构信息
        /// </summary>
        private void LogPattern1Structure()
        {
            if (_pattern1Template == null) return;

            Log.Info("=== Pattern 1 结构分析 ===");

            // 获取 FSM
            var patternControlFsm = FSMUtility.LocateMyFSM(_pattern1Template, "silk_boss_pattern_control");
            if (patternControlFsm != null)
            {
                Log.Info($"找到 silk_boss_pattern_control FSM");
            }
            else
            {
                Log.Warn("未找到 silk_boss_pattern_control FSM");
            }

            // 获取所有 Silk Boss WebStrand 子物品
            int webStrandCount = 0;
            foreach (Transform child in _pattern1Template.transform)
            {
                if (child.name.Contains("Silk Boss WebStrand"))
                {
                    webStrandCount++;

                    // 只详细记录第一个 WebStrand
                    if (webStrandCount == 1)
                    {
                        Log.Info($"--- 分析第一个 WebStrand: {child.name} ---");

                        // 获取 FSM
                        var controlFsm = FSMUtility.LocateMyFSM(child.gameObject, "Control");
                        var hornetCatchFsm = FSMUtility.LocateMyFSM(child.gameObject, "Hornet Catch");

                        if (controlFsm != null)
                            Log.Info("  找到 Control FSM");
                        if (hornetCatchFsm != null)
                            Log.Info("  找到 Hornet Catch FSM");

                        // 获取子物品
                        var webStrandSingle = child.Find("web_strand_single");
                        var webStrandCaught = child.Find("web_strand_caught");

                        if (webStrandSingle != null)
                            Log.Info($"  找到子物品: web_strand_single");
                        if (webStrandCaught != null)
                            Log.Info($"  找到子物品: web_strand_caught");
                    }
                }
            }

            Log.Info($"总共找到 {webStrandCount} 个 Silk Boss WebStrand 子物品");
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
            _singleWebStrandPrefab.transform.SetScaleX(2f);

            // 永久保存，不随场景销毁
            DontDestroyOnLoad(_singleWebStrandPrefab);

            // 立即禁用（作为预制体模板）
            _singleWebStrandPrefab.SetActive(false);

            // 配置预制体：添加 DamageHero 和基础组件
            ConfigureWebStrandPrefab();

            Log.Info($"=== 单根丝线预制体创建完成: {_singleWebStrandPrefab.name} ===");
            yield return null;
        }


        /// <summary>
        /// 创建对象池容器
        /// </summary>
        private void CreatePoolContainer()
        {
            _poolContainer = new GameObject("SingleWeb Pool");
            _poolContainer.transform.SetParent(transform);
            // DontDestroyOnLoad(_poolContainer);
            Log.Info("已创建对象池容器（不随场景销毁）");
        }

        /// <summary>
        /// 分析原版 FSM（使用 FsmAnalyzer）
        /// </summary>
        private void AnalyzeOriginalFSMs()
        {
            if (_pattern1Template == null)
            {
                Log.Warn("Pattern 1 模板为 null，跳过 FSM 分析");
                return;
            }

            Log.Info("=== 开始分析原版 FSM ===");

            // 1. 分析 silk_boss_pattern_control FSM
            var patternControlFsm = FSMUtility.LocateMyFSM(_pattern1Template, "silk_boss_pattern_control");
            if (patternControlFsm != null)
            {
                string outputPath1 = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\silk_boss_pattern_control.txt";
                FsmAnalyzer.WriteFsmReport(patternControlFsm, outputPath1);
                Log.Info($"已导出 silk_boss_pattern_control FSM 到: {outputPath1}");
            }

            // 2. 找到第一个 Silk Boss WebStrand
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
                Log.Warn("未找到 Silk Boss WebStrand，无法分析其 FSM");
                return;
            }

            // 3. 分析 Control FSM
            var controlFsm = FSMUtility.LocateMyFSM(firstWebStrand, "Control");
            if (controlFsm != null)
            {
                string outputPath2 = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\web_strand_control.txt";
                FsmAnalyzer.WriteFsmReport(controlFsm, outputPath2);
                Log.Info($"已导出 WebStrand Control FSM 到: {outputPath2}");
            }

            // 4. 分析 Hornet Catch FSM
            var hornetCatchFsm = FSMUtility.LocateMyFSM(firstWebStrand, "Hornet Catch");
            if (hornetCatchFsm != null)
            {
                string outputPath3 = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\web_strand_hornet_catch.txt";
                FsmAnalyzer.WriteFsmReport(hornetCatchFsm, outputPath3);
                Log.Info($"已导出 WebStrand Hornet Catch FSM 到: {outputPath3}");
            }

            Log.Info("=== FSM 分析完成 ===");
        }
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
            
            Log.Info($"创建新丝线实例: {webInstance.name}，当前池大小: {_webPool.Count}");
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

            Log.Debug($"已生成并触发丝线攻击: {webBehavior.gameObject.name} at {position}");
            return webBehavior;
        }

        /// <summary>
        /// 简化版：只指定位置
        /// </summary>
        public SingleWebBehavior? SpawnAndAttack(Vector3 position)
        {
            return SpawnAndAttack(position, null, null, 0f, 0.75f);
        }

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

            Log.Info("=== 配置单根丝线预制体 ===");

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
                        Log.Info("已从 DamageHeroEventManager 设置预制体 DamageHero 事件");
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
        private void CleanupPrefab()
        {
            Log.Info("=== 开始清理 SingleWebManager 缓存 ===");

            // 停止所有协程
            StopAllCoroutines();

            // 清理对象池：销毁所有实例
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
                Log.Info($"已销毁对象池中的 {destroyedCount} 个丝线实例");
            }

            // 销毁对象池容器
            if (_poolContainer != null)
            {
                Object.Destroy(_poolContainer);
                _poolContainer = null;
                Log.Info("已销毁对象池容器");
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

        #region Testing & Debugging
        /// <summary>
        /// 测试单根丝线生成和攻击（按 Y 键触发）
        /// </summary>
        private void TestSingleWebStrand()
        {
            Log.Info("=== 开始单根丝线测试（新对象池系统）===");

            // 查找玩家
            var hero = FindFirstObjectByType<HeroController>();
            if (hero == null)
            {
                Log.Error("未找到玩家，测试失败");
                return;
            }

            // 测试1：玩家上方单根丝线
            Vector3 testPos = hero.transform.position + new Vector3(0f, 3f, 0f);
            var web = SpawnAndAttack(testPos);

            if (web != null)
            {
                Log.Info("单根丝线测试成功（玩家上方 3 单位）");
            }

            // 测试2：3秒后批量生成（验证冷却系统）
            StartCoroutine(TestMultipleWebStrands(hero));
        }

        /// <summary>
        /// 测试批量生成多根丝线（验证对象池复用）
        /// </summary>
        private IEnumerator TestMultipleWebStrands(HeroController hero)
        {
            // 延迟 3 秒（验证第一根丝线是否能被复用）
            yield return new WaitForSeconds(3f);

            Log.Info("=== 开始批量丝线测试（5根圆形分布）===");

            // 在玩家周围圆形分布生成 5 根丝线
            int count = 5;
            float radius = 8f;
            Vector3[] positions = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count);
                positions[i] = hero.transform.position + new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius,
                    0f
                );
            }

            // 批量生成并触发攻击
            var webs = SpawnMultipleAndAttack(positions);


        }
        #endregion
    }
}

