using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// First Weaver 资源管理器
    /// 负责从 Slab_10b 场景加载 Pin Projectile 和 Blast 物品
    /// 
    /// 生命周期：
    /// - 进入 BOSS 场景 (Cradle_03) 时：从场景加载预制体，创建对象池
    /// - 离开 BOSS 场景时：销毁预制体和对象池内的所有物品
    /// 
    /// Pin Projectile 说明：
    /// - FSM 的 Init 状态完成后自动进入 Dormant 状态
    /// - Dormant 状态会自动隐藏 MeshRenderer，所以实例化后应该 SetActive(true) 让 FSM 正常运行
    /// </summary>
    internal class FirstWeaverManager : MonoBehaviour
    {
        #region 常量配置
        /// <summary>资源场景名称（First Weaver 场景）</summary>
        private const string SOURCE_SCENE_NAME = "Slab_10b";

        /// <summary>BOSS 场景名称</summary>
        private const string BOSS_SCENE_NAME = "Cradle_03";

        /// <summary>Pin Projectile 路径</summary>
        private const string PIN_PROJECTILE_PATH = "Boss Scene/Pin Projectiles/FW Pin Projectile (3)";

        /// <summary>Blast 路径</summary>
        private const string BLAST_PATH = "Boss Scene/Blasts/First Weaver Blast";

        /// <summary>FSM 分析输出路径</summary>
        private const string FSM_OUTPUT_PATH = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\";

        /// <summary>Pin Projectile 对象池大小</summary>
        private const int PIN_POOL_SIZE = 20;

        /// <summary>Blast 对象池大小</summary>
        private const int BLAST_POOL_SIZE = 10;
        #endregion

        #region 字段
        /// <summary>Pin Projectile 预制体</summary>
        private GameObject? _pinProjectilePrefab;

        /// <summary>Blast 预制体</summary>
        private GameObject? _blastPrefab;

        /// <summary>Pin Projectile 对象池</summary>
        private readonly List<GameObject> _pinProjectilePool = new();

        /// <summary>Blast 对象池</summary>
        private readonly List<GameObject> _blastPool = new();

        /// <summary>对象池容器</summary>
        private GameObject? _poolContainer;

        /// <summary>初始化标志</summary>
        private bool _initialized = false;

        /// <summary>正在初始化标志</summary>
        private bool _initializing = false;

        /// <summary>动画资源 GameObject</summary>
        private GameObject? _firstWeaverAnimGO;

        /// <summary>tk2dSpriteAnimation 组件引用</summary>
        private tk2dSpriteAnimation? _pinAnimation;

        /// <summary>tk2dSpriteCollectionData 引用</summary>
        private tk2dSpriteCollectionData? _pinSpriteCollection;

        /// <summary>AssetManager 引用</summary>
        private AssetManager? _assetManager;
        #endregion

        #region 属性
        /// <summary>Pin Projectile 预制体</summary>
        public GameObject? PinProjectilePrefab => _pinProjectilePrefab;

        /// <summary>Blast 预制体</summary>
        public GameObject? BlastPrefab => _blastPrefab;

        /// <summary>是否已初始化</summary>
        public bool IsInitialized => _initialized;
        #endregion

        #region 生命周期
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

        private void OnDestroy()
        {
            CleanupAllPools();
        }
        #endregion

        #region 场景管理
        /// <summary>
        /// 场景加载回调（进入场景）
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 只在 BOSS 场景时处理
            if (scene.name != BOSS_SCENE_NAME)
            {
                return;
            }

            // 每次进入 BOSS 场景都重新初始化
            Log.Info($"[FirstWeaverManager] 检测到 BOSS 场景 {scene.name}，开始初始化...");
            StartCoroutine(Initialize());
        }

        /// <summary>
        /// 场景切换回调（离开场景）
        /// </summary>
        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            // 当离开 BOSS 场景时清理
            if (oldScene.name == BOSS_SCENE_NAME)
            {
                Log.Info($"[FirstWeaverManager] 离开 BOSS 场景 {oldScene.name}，清理缓存");
                CleanupAllPools();
            }
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化管理器（异步加载场景并提取物品）
        /// </summary>
        public IEnumerator Initialize()
        {
            if (_initialized || _initializing)
            {
                Log.Info("[FirstWeaverManager] 已初始化或正在初始化，跳过");
                yield break;
            }

            _initializing = true;
            Log.Info("[FirstWeaverManager] 开始初始化...");

            // 等待场景加载完成
            yield return new WaitForSeconds(0.2f);

            // 1. 一次性加载 Pin/Blast
            yield return LoadPrefabsOnce();

            // 2. 加载动画资源并修复引用
            yield return LoadAndFixAnimationReferences();

            // 3. 分析 FSM（如果加载成功）
            AnalyzePrefabsFsm();

            // 4. 创建对象池容器
            CreatePoolContainer();

            // 5. 预生成对象池
            yield return PrewarmPools();

            _initialized = true;
            _initializing = false;
            Log.Info("[FirstWeaverManager] 初始化完成");
        }

        /// <summary>
        /// 一次性加载 Pin 和 Blast（单次场景加载）
        /// 仅在进入 BOSS 场景时调用，离开时自动销毁
        /// </summary>
        private IEnumerator LoadPrefabsOnce()
        {
            Log.Info($"[FirstWeaverManager] 批量加载资源: {PIN_PROJECTILE_PATH} | {BLAST_PATH}");

            var task = SceneObjectManager.LoadObjectsFromScene(SOURCE_SCENE_NAME, PIN_PROJECTILE_PATH, BLAST_PATH);

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.Exception != null)
            {
                Log.Error($"[FirstWeaverManager] 批量加载失败: {task.Exception}");
                yield break;
            }

            var dict = task.Result;

            dict.TryGetValue(PIN_PROJECTILE_PATH, out _pinProjectilePrefab);
            dict.TryGetValue(BLAST_PATH, out _blastPrefab);

            if (_pinProjectilePrefab != null)
            {
                _pinProjectilePrefab.name = "FW Pin Projectile Prefab";
                // 预制体保持禁用状态作为模板，实例化后的对象会激活让 FSM 运行
                _pinProjectilePrefab.SetActive(false);
                _pinProjectilePrefab.transform.SetParent(transform);
                Log.Info($"[FirstWeaverManager] Pin Projectile 加载成功: {_pinProjectilePrefab.name}");
            }
            else
            {
                Log.Error("[FirstWeaverManager] Pin Projectile 加载失败：返回 null");
            }

            if (_blastPrefab != null)
            {
                _blastPrefab.name = "First Weaver Blast Prefab";
                // 预制体保持禁用状态作为模板
                _blastPrefab.SetActive(false);
                _blastPrefab.transform.SetParent(transform);
                Log.Info($"[FirstWeaverManager] Blast 加载成功: {_blastPrefab.name}");
            }
            else
            {
                Log.Error("[FirstWeaverManager] Blast 加载失败：返回 null");
            }
        }

        /// <summary>
        /// 加载动画资源并修复预制体的动画引用
        /// 场景卸载后 tk2dSpriteAnimation 资源会失效，需要重新加载
        /// </summary>
        private IEnumerator LoadAndFixAnimationReferences()
        {
            Log.Info("[FirstWeaverManager] 开始加载动画资源...");

            // 获取 AssetManager
            _assetManager = FindFirstObjectByType<AssetManager>();
            if (_assetManager == null)
            {
                Log.Error("[FirstWeaverManager] 未找到 AssetManager，无法加载动画资源");
                yield break;
            }

            // 加载 First Weaver Anim GameObject
            _firstWeaverAnimGO = _assetManager.Get<GameObject>("First Weaver Anim");
            if (_firstWeaverAnimGO == null)
            {
                Log.Error("[FirstWeaverManager] 无法加载 First Weaver Anim");
                yield break;
            }

            // 获取 tk2dSpriteAnimation 组件
            _pinAnimation = _firstWeaverAnimGO.GetComponent<tk2dSpriteAnimation>();
            if (_pinAnimation == null)
            {
                Log.Error("[FirstWeaverManager] First Weaver Anim 未包含 tk2dSpriteAnimation 组件");
                yield break;
            }

            Log.Info($"[FirstWeaverManager] 动画资源加载成功: {_firstWeaverAnimGO.name}");

            // 搜索并加载 sprite collection
            yield return LoadSpriteCollection();

            // 修复 Pin Projectile 预制体的动画引用
            FixPrefabAnimationReference(_pinProjectilePrefab);

            Log.Info("[FirstWeaverManager] 动画资源加载并修复完成");
        }

        /// <summary>
        /// 加载 sprite collection
        /// </summary>
        private IEnumerator LoadSpriteCollection()
        {
            if (_assetManager == null) yield break;

            Log.Info("[FirstWeaverManager] 开始加载 sprite collection...");

            // 加载 First Weaver Cln GameObject
            var spriteCollectionGO = _assetManager.Get<GameObject>("First Weaver Cln");
            if (spriteCollectionGO == null)
            {
                Log.Error("[FirstWeaverManager] 无法加载 First Weaver Cln");
                yield break;
            }

            // 获取 tk2dSpriteCollectionData 组件
            _pinSpriteCollection = spriteCollectionGO.GetComponent<tk2dSpriteCollectionData>();
            if (_pinSpriteCollection == null)
            {
                Log.Error("[FirstWeaverManager] First Weaver Cln 未包含 tk2dSpriteCollectionData 组件");
                yield break;
            }

            Log.Info($"[FirstWeaverManager] sprite collection 加载成功: {_pinSpriteCollection.name}");

            // 修复动画中所有帧的 spriteCollection 引用
            FixAnimationFrameSpriteCollections();
        }

        /// <summary>
        /// 修复动画中所有帧的 spriteCollection 引用
        /// 从 bundle 加载的动画，其帧中的 spriteCollection 引用会失效，需要重新设置
        /// </summary>
        private void FixAnimationFrameSpriteCollections()
        {
            if (_pinAnimation == null || _pinSpriteCollection == null)
            {
                Log.Warn("[FirstWeaverManager] 无法修复动画帧: _pinAnimation 或 _pinSpriteCollection 为 null");
                return;
            }

            if (_pinAnimation.clips == null)
            {
                Log.Warn("[FirstWeaverManager] 动画没有 clips");
                return;
            }

            int fixedFrameCount = 0;
            foreach (var clip in _pinAnimation.clips)
            {
                if (clip == null || clip.frames == null) continue;

                foreach (var frame in clip.frames)
                {
                    if (frame != null)
                    {
                        frame.spriteCollection = _pinSpriteCollection;
                        fixedFrameCount++;
                    }
                }
            }

            Log.Info($"[FirstWeaverManager] 已修复 {_pinAnimation.clips.Length} 个动画 clip 中的 {fixedFrameCount} 帧的 spriteCollection 引用");
        }

        /// <summary>
        /// 修复预制体及其子对象的动画引用
        /// </summary>
        /// <param name="prefab">需要修复的预制体</param>
        private void FixPrefabAnimationReference(GameObject? prefab)
        {
            if (prefab == null || _pinAnimation == null) return;

            // 使用已加载的 _pinSpriteCollection
            tk2dSpriteCollectionData? spriteCollection = _pinSpriteCollection;

            // 修复主对象的 tk2dSpriteAnimator 和 tk2dSprite
            var mainAnimator = prefab.GetComponent<tk2dSpriteAnimator>();
            if (mainAnimator != null)
            {
                mainAnimator.Library = _pinAnimation;
                Log.Debug($"[FirstWeaverManager] 已修复 {prefab.name} tk2dSpriteAnimator.Library");
            }

            var mainSprite = prefab.GetComponent<tk2dSprite>();
            if (mainSprite != null && spriteCollection != null)
            {
                mainSprite.Collection = spriteCollection;
                // 重新构建 sprite 的 mesh 数据
                mainSprite.Build();
                Log.Debug($"[FirstWeaverManager] 已修复并重建 {prefab.name} tk2dSprite");
            }
            else if (mainSprite != null && spriteCollection == null)
            {
                Log.Warn($"[FirstWeaverManager] 无法修复 {prefab.name} tk2dSprite.Collection: spriteCollection 为 null");
            }

            // 修复 Thread 子对象的 tk2dSpriteAnimator 和 tk2dSprite
            var threadTransform = prefab.transform.Find("Thread");
            if (threadTransform != null)
            {
                var threadAnimator = threadTransform.GetComponent<tk2dSpriteAnimator>();
                if (threadAnimator != null)
                {
                    threadAnimator.Library = _pinAnimation;
                    Log.Debug("[FirstWeaverManager] 已修复 Thread tk2dSpriteAnimator.Library");
                }

                var threadSprite = threadTransform.GetComponent<tk2dSprite>();
                if (threadSprite != null && spriteCollection != null)
                {
                    threadSprite.Collection = spriteCollection;
                    // 重新构建 sprite 的 mesh 数据
                    threadSprite.Build();
                    Log.Debug("[FirstWeaverManager] 已修复并重建 Thread tk2dSprite");
                }
            }
        }


        /// <summary>
        /// 分析预制体的 FSM 并输出报告
        /// </summary>
        private void AnalyzePrefabsFsm()
        {
            // 分析 Pin Projectile FSM
            if (_pinProjectilePrefab != null)
            {
                var pinFsm = _pinProjectilePrefab.LocateMyFSM("Control");
                if (pinFsm != null)
                {
                    string outputPath = FSM_OUTPUT_PATH + "_pinProjectileFsm.txt";
                    FsmAnalyzer.WriteFsmReport(pinFsm, outputPath);
                    Log.Info($"[FirstWeaverManager] Pin Projectile FSM 分析完成: {outputPath}");
                }
                else
                {
                    Log.Warn("[FirstWeaverManager] Pin Projectile 未找到 Control FSM");
                }
            }

            // 分析 Blast FSM
            if (_blastPrefab != null)
            {
                var blastFsm = _blastPrefab.LocateMyFSM("Control");
                if (blastFsm != null)
                {
                    string outputPath = FSM_OUTPUT_PATH + "_blastFsm.txt";
                    FsmAnalyzer.WriteFsmReport(blastFsm, outputPath);
                    Log.Info($"[FirstWeaverManager] Blast FSM 分析完成: {outputPath}");
                }
                else
                {
                    Log.Warn("[FirstWeaverManager] Blast 未找到 Control FSM");
                }
            }
        }

        /// <summary>
        /// 创建对象池容器（保持 Active）
        /// </summary>
        private void CreatePoolContainer()
        {
            if (_poolContainer == null)
            {
                _poolContainer = new GameObject("FirstWeaverPool");
                _poolContainer.transform.SetParent(transform);
                // 注意：对象池容器保持 Active，便于 FSM 正常初始化
                Log.Info("[FirstWeaverManager] 对象池容器已创建");
            }
        }

        /// <summary>
        /// 预生成对象池
        /// </summary>
        private IEnumerator PrewarmPools()
        {
            Log.Info("[FirstWeaverManager] 开始预生成对象池...");

            // 预生成 Pin Projectile 池
            if (_pinProjectilePrefab != null)
            {
                for (int i = 0; i < PIN_POOL_SIZE; i++)
                {
                    var obj = CreatePinProjectileInstance();
                    if (obj != null)
                    {
                        _pinProjectilePool.Add(obj);
                    }

                    // 每生成5个暂停一帧，避免卡顿
                    if (i % 5 == 0)
                    {
                        yield return null;
                    }
                }
                Log.Info($"[FirstWeaverManager] Pin Projectile 池预生成完成: {_pinProjectilePool.Count} 个");
            }

            // 预生成 Blast 池
            if (_blastPrefab != null)
            {
                for (int i = 0; i < BLAST_POOL_SIZE; i++)
                {
                    var obj = CreateBlastInstance();
                    if (obj != null)
                    {
                        _blastPool.Add(obj);
                    }

                    // 每生成5个暂停一帧
                    if (i % 5 == 0)
                    {
                        yield return null;
                    }
                }
                Log.Info($"[FirstWeaverManager] Blast 池预生成完成: {_blastPool.Count} 个");
            }
        }
        #endregion

        #region 对象池操作
        /// <summary>
        /// 创建 Pin Projectile 实例
        /// 实例化后激活对象让 FSM 运行，FSM 会自动进入 Dormant 状态并隐藏 MeshRenderer
        /// 注意：预制体的 Collection 和 Library 已在初始化时设置，实例会继承这些引用
        /// tk2dSprite.Awake() 会自动调用 Build() 初始化 mesh
        /// </summary>
        private GameObject? CreatePinProjectileInstance()
        {
            if (_pinProjectilePrefab == null || _poolContainer == null)
            {
                return null;
            }

            var obj = Instantiate(_pinProjectilePrefab, _poolContainer.transform);
            obj.name = $"FW Pin Projectile (Pool)";
            
            // 激活对象让 FSM 正常初始化
            // FSM 的 Init 状态完成后会自动进入 Dormant 状态，Dormant 状态会隐藏 MeshRenderer
            // tk2dSprite.Awake() 会自动检测 Collection 并调用 Build()
            obj.SetActive(true);

            // 补丁 Pin FSM，添加 DIRECT_FIRE 全局转换
            PatchPinFsm(obj);

            return obj;
        }

        /// <summary>
        /// 补丁 Pin Projectile FSM，添加 DIRECT_FIRE 全局转换到 Antic 状态
        /// 这样外部可以直接触发 Pin 进入 Antic 状态，跳过原版的 Dormant → Attack Pause → Move Pin 流程
        /// </summary>
        /// <param name="pinObj">Pin Projectile 实例</param>
        private void PatchPinFsm(GameObject pinObj)
        {
            var fsm = pinObj.LocateMyFSM("Control");
            if (fsm == null)
            {
                Log.Warn("[FirstWeaverManager] Pin Projectile 未找到 Control FSM，无法补丁");
                return;
            }

            // 1. 创建或获取 DIRECT_FIRE 事件
            var directFireEvent = FsmEvent.GetFsmEvent("DIRECT_FIRE");

            // 2. 添加事件到 FSM 的事件列表
            var events = fsm.FsmEvents.ToList();
            if (!events.Contains(directFireEvent))
            {
                events.Add(directFireEvent);
                // 使用反射设置 FsmEvents
                var fsmType = fsm.Fsm.GetType();
                var eventsField = fsmType.GetField("events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (eventsField != null)
                {
                    eventsField.SetValue(fsm.Fsm, events.ToArray());
                }
            }

            // 3. 查找 Antic 状态
            var anticState = fsm.FsmStates.FirstOrDefault(s => s.Name == "Antic");
            if (anticState == null)
            {
                Log.Warn("[FirstWeaverManager] Pin FSM 未找到 Antic 状态，无法添加全局转换");
                return;
            }

            // 4. 创建全局转换 DIRECT_FIRE → Antic
            var globalTransition = new FsmTransition
            {
                FsmEvent = directFireEvent,
                toState = "Antic",
                toFsmState = anticState
            };

            // 5. 添加全局转换
            var globalTransitions = fsm.FsmGlobalTransitions.ToList();
            // 检查是否已存在
            if (!globalTransitions.Any(t => t.FsmEvent == directFireEvent))
            {
                globalTransitions.Add(globalTransition);

                // 使用反射设置 FsmGlobalTransitions
                var fsmType = fsm.Fsm.GetType();
                var globalTransitionsField = fsmType.GetField("globalTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (globalTransitionsField != null)
                {
                    globalTransitionsField.SetValue(fsm.Fsm, globalTransitions.ToArray());
                }
            }

            // 7. 在 Release Pin 状态末尾添加回收动作，自动回收到池
            var releaseState = fsm.FsmStates.FirstOrDefault(s => s.Name == "Release Pin");
            if (releaseState != null)
            {
                var actions = releaseState.Actions.ToList();
                var recycleAction = new CallMethod
                {
                    behaviour = this,
                    methodName = "RecyclePinProjectile",
                    parameters = new[]
                    {
                        new FsmVar
                        {
                            type = VariableType.GameObject,
                            useVariable = true,
                            variableName = "Self"
                        }
                    },
                    everyFrame = false
                };
                actions.Add(recycleAction);
                releaseState.Actions = actions.ToArray();
            }

            // 6. 初始化 FSM 数据（确保补丁生效）
            fsm.Fsm.InitData();

            Log.Debug($"[FirstWeaverManager] Pin FSM 已补丁，添加 DIRECT_FIRE → Antic 全局转换");
        }

        /// <summary>
        /// 创建 Blast 实例
        /// </summary>
        private GameObject? CreateBlastInstance()
        {
            if (_blastPrefab == null || _poolContainer == null)
            {
                return null;
            }

            var obj = Instantiate(_blastPrefab, _poolContainer.transform);
            obj.name = $"First Weaver Blast (Pool)";
            obj.SetActive(false);
            return obj;
        }

        /// <summary>
        /// 从池中获取或创建 Pin Projectile
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <param name="parent">父对象（可选）</param>
        /// <returns>Pin Projectile 实例</returns>
        public GameObject? SpawnPinProjectile(Vector3 position, Transform? parent = null)
        {
            if (!_initialized)
            {
                Log.Warn("[FirstWeaverManager] 尚未初始化，无法生成 Pin Projectile");
                return null;
            }

            GameObject? obj = null;

            // 尝试从池中获取
            if (_pinProjectilePool.Count > 0)
            {
                obj = _pinProjectilePool[_pinProjectilePool.Count - 1];
                _pinProjectilePool.RemoveAt(_pinProjectilePool.Count - 1);
            }
            else
            {
                // 池为空，创建新实例
                obj = CreatePinProjectileInstance();
                Log.Debug("[FirstWeaverManager] Pin Projectile 池为空，创建新实例");
            }

            if (obj != null)
            {
                obj.transform.SetParent(parent);
                obj.transform.position = position;
                obj.SetActive(true);

                // 初始化 FSM（确保 FSM 处于正确状态）
                var fsm = obj.LocateMyFSM("Control");
                if (fsm != null)
                {
                    // 重新初始化 FSM 确保状态正确
                    fsm.Fsm.InitData();
                }
            }

            return obj;
        }

        /// <summary>
        /// 从池中获取或创建 Blast
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <param name="parent">父对象（可选）</param>
        /// <returns>Blast 实例</returns>
        public GameObject? SpawnBlast(Vector3 position, Transform? parent = null)
        {
            if (!_initialized)
            {
                Log.Warn("[FirstWeaverManager] 尚未初始化，无法生成 Blast");
                return null;
            }

            GameObject? obj = null;

            // 尝试从池中获取
            if (_blastPool.Count > 0)
            {
                obj = _blastPool[_blastPool.Count - 1];
                _blastPool.RemoveAt(_blastPool.Count - 1);
            }
            else
            {
                // 池为空，创建新实例
                obj = CreateBlastInstance();
                Log.Debug("[FirstWeaverManager] Blast 池为空，创建新实例");
            }

            if (obj != null)
            {
                obj.transform.SetParent(parent);
                obj.transform.position = position;
                obj.SetActive(true);

                // 触发 FSM 开始
                var fsm = obj.LocateMyFSM("Control");
                if (fsm != null)
                {
                    fsm.Fsm.InitData();
                    fsm.SendEvent("BLAST");
                }
            }

            return obj;
        }

        /// <summary>
        /// 回收 Pin Projectile 到池
        /// 不禁用对象，让 FSM 保持在 Dormant 状态（自动隐藏）
        /// </summary>
        /// <param name="obj">要回收的对象</param>
        public void RecyclePinProjectile(GameObject obj)
        {
            if (obj == null || _poolContainer == null)
            {
                return;
            }

            obj.transform.SetParent(_poolContainer.transform);
            _pinProjectilePool.Add(obj);
        }

        /// <summary>
        /// 回收 Blast 到池
        /// </summary>
        /// <param name="obj">要回收的对象</param>
        public void RecycleBlast(GameObject obj)
        {
            if (obj == null || _poolContainer == null)
            {
                return;
            }

            obj.SetActive(false);
            obj.transform.SetParent(_poolContainer.transform);
            _blastPool.Add(obj);
        }

        /// <summary>
        /// 回收所有活跃的 Pin Projectile
        /// </summary>
        public void RecycleAllPinProjectiles()
        {
            // 查找所有活跃的 Pin Projectile 并回收
            var activeObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in activeObjects)
            {
                if (obj.name.Contains("FW Pin Projectile") && obj.activeInHierarchy && obj.transform.parent != _poolContainer?.transform)
                {
                    RecyclePinProjectile(obj);
                }
            }
        }

        /// <summary>
        /// 回收所有活跃的 Blast
        /// </summary>
        public void RecycleAllBlasts()
        {
            var activeObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in activeObjects)
            {
                if (obj.name.Contains("First Weaver Blast") && obj.activeInHierarchy && obj.transform.parent != _poolContainer?.transform)
                {
                    RecycleBlast(obj);
                }
            }
        }
        #endregion

        #region 清理
        /// <summary>
        /// 清理所有对象池（离开 BOSS 场景时调用）
        /// </summary>
        private void CleanupAllPools()
        {
            Log.Info("[FirstWeaverManager] 开始清理对象池...");

            // 停止所有协程
            StopAllCoroutines();

            // 清理 Pin Projectile 池
            foreach (var obj in _pinProjectilePool)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            _pinProjectilePool.Clear();

            // 清理 Blast 池
            foreach (var obj in _blastPool)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            _blastPool.Clear();

            // 销毁池容器
            if (_poolContainer != null)
            {
                Destroy(_poolContainer);
                _poolContainer = null;
            }

            // 销毁预制体
            if (_pinProjectilePrefab != null)
            {
                Destroy(_pinProjectilePrefab);
                _pinProjectilePrefab = null;
            }

            if (_blastPrefab != null)
            {
                Destroy(_blastPrefab);
                _blastPrefab = null;
            }

            // 清理动画资源引用（不销毁，因为是从 AssetManager 获取的）
            _firstWeaverAnimGO = null;
            _pinAnimation = null;
            _pinSpriteCollection = null;
            _assetManager = null;

            // 重置初始化标志
            _initialized = false;
            _initializing = false;

            Log.Info("[FirstWeaverManager] 对象池清理完成");
        }
        #endregion
    }
}
