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
    /// First Weaver Pin Projectile 管理器
    /// 负责管理 Pin Projectile 物品的对象池
    /// 
    /// 生命周期：
    /// - 进入 BOSS 场景 (Cradle_03) 时：通过 AssetManager 获取预制体，创建对象池
    /// - 离开 BOSS 场景时：销毁对象池内的所有物品（预制体由 AssetManager 持久化管理）
    /// 
    /// Pin Projectile 说明：
    /// - FSM 的 Init 状态完成后自动进入 Dormant 状态
    /// - Dormant 状态会自动隐藏 MeshRenderer，所以实例化后应该 SetActive(true) 让 FSM 正常运行
    /// - 需要加载动画资源并修复引用（tk2dSpriteAnimation 和 tk2dSpriteCollectionData）
    /// </summary>
    internal class FWPinManager : MonoBehaviour
    {
        #region 常量配置
        /// <summary>BOSS 场景名称</summary>
        private const string BOSS_SCENE_NAME = "Cradle_03";

        /// <summary>AssetManager 中的 Pin Projectile 资源名称</summary>
        private const string PIN_PROJECTILE_ASSET_NAME = "FW Pin Projectile";

        /// <summary>FSM 分析输出路径</summary>
        private const string FSM_OUTPUT_PATH = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\";

        /// <summary>Pin Projectile 对象池大小</summary>
        private const int PIN_POOL_SIZE = 50;
        #endregion

        #region 字段
        /// <summary>Pin Projectile 预制体（从 AssetManager 获取）</summary>
        private GameObject? _pinProjectilePrefab;

        /// <summary>Pin Projectile 对象池</summary>
        private readonly List<GameObject> _pinProjectilePool = new();

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

        /// <summary>是否已初始化</summary>
        public bool IsInitialized => _initialized;
        #endregion

        #region 生命周期
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnDestroy()
        {
            CleanupPool();
        }
        #endregion

        #region 场景管理
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != BOSS_SCENE_NAME)
            {
                return;
            }

            Log.Info($"[FWPinManager] 检测到 BOSS 场景 {scene.name}，开始初始化...");
            StartCoroutine(Initialize());
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            // 只有真正离开 BOSS 场景（到其他场景）时才清理，同场景重载（如死亡复活）不清理
            if (oldScene.name == BOSS_SCENE_NAME && newScene.name != BOSS_SCENE_NAME)
            {
                Log.Info($"[FWPinManager] 离开 BOSS 场景 {oldScene.name}，清理缓存");
                CleanupPool();
            }
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化管理器
        /// </summary>
        public IEnumerator Initialize()
        {
            if (_initialized || _initializing)
            {
                Log.Info("[FWPinManager] 已初始化或正在初始化，跳过");
                yield break;
            }

            _initializing = true;
            Log.Info("[FWPinManager] 开始初始化...");

            yield return new WaitForSeconds(0.2f);

            // 1. 获取 AssetManager 并加载预制体
            yield return LoadPrefabFromAssetManager();

            // 2. 加载动画资源并修复引用
            yield return LoadAndFixAnimationReferences();

            // 3. 分析 FSM
            AnalyzePrefabFsm();

            // 4. 创建对象池容器
            CreatePoolContainer();

            // 5. 预生成对象池
            yield return PrewarmPool();

            _initialized = true;
            _initializing = false;
            Log.Info("[FWPinManager] 初始化完成");
        }

        private IEnumerator LoadPrefabFromAssetManager()
        {
            Log.Info($"[FWPinManager] 从 AssetManager 获取预制体...");

            _assetManager = GetComponent<AssetManager>();
            if (_assetManager == null)
            {
                var managerObj = GameObject.Find("AnySilkBossManager");
                if (managerObj != null)
                {
                    _assetManager = managerObj.GetComponent<AssetManager>();
                }
            }

            if (_assetManager == null)
            {
                Log.Error("[FWPinManager] 未找到 AssetManager，无法加载预制体");
                yield break;
            }

            if (!_assetManager.IsExternalAssetsPreloaded)
            {
                Log.Info("[FWPinManager] 等待 AssetManager 预加载完成...");
                yield return _assetManager.WaitForPreload();
            }

            _pinProjectilePrefab = _assetManager.GetSceneObject(PIN_PROJECTILE_ASSET_NAME);

            if (_pinProjectilePrefab != null)
            {
                Log.Info($"[FWPinManager] Pin Projectile 获取成功: {_pinProjectilePrefab.name}");
            }
            else
            {
                Log.Error("[FWPinManager] Pin Projectile 获取失败：返回 null");
            }
        }

        /// <summary>
        /// 加载动画资源并修复预制体的动画引用
        /// 场景卸载后 tk2dSpriteAnimation 资源会失效，需要重新加载
        /// </summary>
        private IEnumerator LoadAndFixAnimationReferences()
        {
            Log.Info("[FWPinManager] 开始加载动画资源...");

            if (_assetManager == null)
            {
                Log.Error("[FWPinManager] AssetManager 为 null，无法加载动画资源");
                yield break;
            }

            if (!_assetManager.IsExternalAssetsPreloaded)
            {
                Log.Info("[FWPinManager] 等待 AssetManager 预加载完成...");
                yield return _assetManager.WaitForPreload();
            }

            // 加载 First Weaver Anim GameObject
            _firstWeaverAnimGO = _assetManager.Get<GameObject>("First Weaver Anim");
            if (_firstWeaverAnimGO == null)
            {
                Log.Error("[FWPinManager] 无法加载 First Weaver Anim");
                yield break;
            }

            // 获取 tk2dSpriteAnimation 组件
            _pinAnimation = _firstWeaverAnimGO.GetComponent<tk2dSpriteAnimation>();
            if (_pinAnimation == null)
            {
                Log.Error("[FWPinManager] First Weaver Anim 未包含 tk2dSpriteAnimation 组件");
                yield break;
            }

            Log.Info($"[FWPinManager] 动画资源加载成功: {_firstWeaverAnimGO.name}");

            // 搜索并加载 sprite collection
            yield return LoadSpriteCollection();

            // 修复 Pin Projectile 预制体的动画引用
            FixPrefabAnimationReference(_pinProjectilePrefab);

            Log.Info("[FWPinManager] 动画资源加载并修复完成");
        }

        /// <summary>
        /// 加载 sprite collection
        /// </summary>
        private IEnumerator LoadSpriteCollection()
        {
            if (_assetManager == null) yield break;

            Log.Info("[FWPinManager] 开始加载 sprite collection...");

            // 加载 First Weaver Cln GameObject
            var spriteCollectionGO = _assetManager.Get<GameObject>("First Weaver Cln");
            if (spriteCollectionGO == null)
            {
                Log.Error("[FWPinManager] 无法加载 First Weaver Cln");
                yield break;
            }

            // 获取 tk2dSpriteCollectionData 组件
            _pinSpriteCollection = spriteCollectionGO.GetComponent<tk2dSpriteCollectionData>();
            if (_pinSpriteCollection == null)
            {
                Log.Error("[FWPinManager] First Weaver Cln 未包含 tk2dSpriteCollectionData 组件");
                yield break;
            }

            Log.Info($"[FWPinManager] sprite collection 加载成功: {_pinSpriteCollection.name}");

            // 修复动画中所有帧的 spriteCollection 引用
            FixAnimationFrameSpriteCollections();
        }

        /// <summary>
        /// 修复动画中所有帧的 spriteCollection 引用
        /// </summary>
        private void FixAnimationFrameSpriteCollections()
        {
            if (_pinAnimation == null || _pinSpriteCollection == null)
            {
                Log.Warn("[FWPinManager] 无法修复动画帧: _pinAnimation 或 _pinSpriteCollection 为 null");
                return;
            }

            if (_pinAnimation.clips == null)
            {
                Log.Warn("[FWPinManager] 动画没有 clips");
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

            Log.Info($"[FWPinManager] 已修复 {_pinAnimation.clips.Length} 个动画 clip 中的 {fixedFrameCount} 帧的 spriteCollection 引用");
        }

        /// <summary>
        /// 修复预制体及其子对象的动画引用
        /// </summary>
        private void FixPrefabAnimationReference(GameObject? prefab)
        {
            if (prefab == null || _pinAnimation == null) return;

            tk2dSpriteCollectionData? spriteCollection = _pinSpriteCollection;

            // 修复主对象的 tk2dSpriteAnimator 和 tk2dSprite
            var mainAnimator = prefab.GetComponent<tk2dSpriteAnimator>();
            if (mainAnimator != null)
            {
                mainAnimator.Library = _pinAnimation;
                Log.Debug($"[FWPinManager] 已修复 {prefab.name} tk2dSpriteAnimator.Library");
            }

            var mainSprite = prefab.GetComponent<tk2dSprite>();
            if (mainSprite != null && spriteCollection != null)
            {
                mainSprite.Collection = spriteCollection;
                mainSprite.Build();
                Log.Debug($"[FWPinManager] 已修复并重建 {prefab.name} tk2dSprite");
            }
            else if (mainSprite != null && spriteCollection == null)
            {
                Log.Warn($"[FWPinManager] 无法修复 {prefab.name} tk2dSprite.Collection: spriteCollection 为 null");
            }

            // 修复 Thread 子对象的 tk2dSpriteAnimator 和 tk2dSprite
            var threadTransform = prefab.transform.Find("Thread");
            if (threadTransform != null)
            {
                var threadAnimator = threadTransform.GetComponent<tk2dSpriteAnimator>();
                if (threadAnimator != null)
                {
                    threadAnimator.Library = _pinAnimation;
                    Log.Debug("[FWPinManager] 已修复 Thread tk2dSpriteAnimator.Library");
                }

                var threadSprite = threadTransform.GetComponent<tk2dSprite>();
                if (threadSprite != null && spriteCollection != null)
                {
                    threadSprite.Collection = spriteCollection;
                    threadSprite.Build();
                    Log.Debug("[FWPinManager] 已修复并重建 Thread tk2dSprite");
                }
            }
        }

        private void AnalyzePrefabFsm()
        {
            if (_pinProjectilePrefab != null)
            {
                var pinFsm = _pinProjectilePrefab.LocateMyFSM("Control");
                if (pinFsm != null)
                {
                    string outputPath = FSM_OUTPUT_PATH + "_pinProjectileFsm.txt";
                    FsmAnalyzer.WriteFsmReport(pinFsm, outputPath);
                    Log.Info($"[FWPinManager] Pin Projectile FSM 分析完成: {outputPath}");
                }
                else
                {
                    Log.Warn("[FWPinManager] Pin Projectile 未找到 Control FSM");
                }
            }
        }

        private void CreatePoolContainer()
        {
            if (_poolContainer == null)
            {
                _poolContainer = new GameObject("FWPinPool");
                _poolContainer.transform.SetParent(transform);
                Log.Info("[FWPinManager] 对象池容器已创建");
            }
        }

        private IEnumerator PrewarmPool()
        {
            Log.Info("[FWPinManager] 开始预生成对象池...");

            if (_pinProjectilePrefab != null)
            {
                for (int i = 0; i < PIN_POOL_SIZE; i++)
                {
                    var obj = CreatePinProjectileInstance();
                    if (obj != null)
                    {
                        _pinProjectilePool.Add(obj);
                    }

                    if (i % 2 == 0)
                    {
                        yield return null;
                    }
                }
                Log.Info($"[FWPinManager] Pin Projectile 池预生成完成: {_pinProjectilePool.Count} 个");
            }
        }
        #endregion

        #region 对象池操作
        /// <summary>
        /// 创建 Pin Projectile 实例
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
            obj.SetActive(true);

            // 补丁 Pin FSM
            PatchPinFsm(obj);

            return obj;
        }

        /// <summary>
        /// 补丁 Pin Projectile FSM
        /// </summary>
        private void PatchPinFsm(GameObject pinObj)
        {
            var fsm = pinObj.LocateMyFSM("Control");
            if (fsm == null)
            {
                Log.Warn("[FWPinManager] Pin Projectile 未找到 Control FSM，无法补丁");
                return;
            }

            // 1. 创建或获取 DIRECT_FIRE 事件
            var directFireEvent = FsmEvent.GetFsmEvent("DIRECT_FIRE");

            // 2. 添加事件到 FSM 的事件列表
            var events = fsm.FsmEvents.ToList();
            if (!events.Contains(directFireEvent))
            {
                events.Add(directFireEvent);
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
                Log.Warn("[FWPinManager] Pin FSM 未找到 Antic 状态，无法添加全局转换");
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
            if (!globalTransitions.Any(t => t.FsmEvent == directFireEvent))
            {
                globalTransitions.Add(globalTransition);

                var fsmType = fsm.Fsm.GetType();
                var globalTransitionsField = fsmType.GetField("globalTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (globalTransitionsField != null)
                {
                    globalTransitionsField.SetValue(fsm.Fsm, globalTransitions.ToArray());
                }
            }

            // 6. 在 Release Pin 状态末尾添加回收动作
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
                            objectReference = pinObj
                        }
                    },
                    everyFrame = false
                };
                actions.Add(recycleAction);
                releaseState.Actions = actions.ToArray();
            }

            // 7. 初始化 FSM 数据
            fsm.Fsm.InitData();

            Log.Debug($"[FWPinManager] Pin FSM 已补丁，添加 DIRECT_FIRE → Antic 全局转换");
        }

        /// <summary>
        /// 从池中获取或创建 Pin Projectile
        /// </summary>
        public GameObject? SpawnPinProjectile(Vector3 position, Transform? parent = null)
        {
            if (!_initialized)
            {
                Log.Warn("[FWPinManager] 尚未初始化，无法生成 Pin Projectile");
                return null;
            }

            GameObject? obj = null;

            if (_pinProjectilePool.Count > 0)
            {
                obj = _pinProjectilePool[_pinProjectilePool.Count - 1];
                _pinProjectilePool.RemoveAt(_pinProjectilePool.Count - 1);
            }
            else
            {
                obj = CreatePinProjectileInstance();
                Log.Debug("[FWPinManager] Pin Projectile 池为空，创建新实例");
            }

            if (obj != null)
            {
                obj.transform.SetParent(parent);
                obj.transform.position = position;
                obj.SetActive(true);

                var fsm = obj.LocateMyFSM("Control");
                if (fsm != null)
                {
                    fsm.Fsm.InitData();
                }
            }

            return obj;
        }

        /// <summary>
        /// 回收 Pin Projectile 到池
        /// </summary>
        public void RecyclePinProjectile(GameObject obj)
        {
            if (obj == null || _poolContainer == null)
            {
                return;
            }

            obj.transform.SetParent(_poolContainer.transform);
            _pinProjectilePool.Add(obj);
            Log.Debug($"[FWPinManager] Pin Projectile 已回收，池中数量: {_pinProjectilePool.Count}");
        }

        /// <summary>
        /// 回收所有活跃的 Pin Projectile
        /// </summary>
        public void RecycleAllPinProjectiles()
        {
            var activeObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in activeObjects)
            {
                if (obj.name.Contains("FW Pin Projectile") && obj.activeInHierarchy && obj.transform.parent != _poolContainer?.transform)
                {
                    RecyclePinProjectile(obj);
                }
            }
        }
        #endregion

        #region 清理
        private void CleanupPool()
        {
            Log.Info("[FWPinManager] 开始清理对象池...");

            StopAllCoroutines();

            foreach (var obj in _pinProjectilePool)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            _pinProjectilePool.Clear();

            if (_poolContainer != null)
            {
                Destroy(_poolContainer);
                _poolContainer = null;
            }

            _pinProjectilePrefab = null;
            _firstWeaverAnimGO = null;
            _pinAnimation = null;
            _pinSpriteCollection = null;
            _initialized = false;
            _initializing = false;

            Log.Info("[FWPinManager] 对象池清理完成");
        }
        #endregion
    }
}
