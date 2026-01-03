using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

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

            // 3. 创建对象池容器
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

            // 搜索并加载 sprite collection
            yield return LoadSpriteCollection();

            // 修复 Pin Projectile 预制体的动画引用
            FixPrefabAnimationReference(_pinProjectilePrefab);

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

            // 池内对象保持“受控休眠”，避免 FSM/动画在池中继续跑导致复用时贴图/状态异常
            var rb2d = obj.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
                rb2d.bodyType = RigidbodyType2D.Kinematic;
            }

            ResetPinVisualState(obj);

            var fsm = obj.LocateMyFSM("Control");
            if (fsm != null)
            {
                ResetPinFsmVariables(fsm);
                fsm.Fsm.InitData();

                var managedDormantState = FindState(fsm, "Managed Dormant");
                if (managedDormantState != null)
                {
                    fsm.SetState(managedDormantState.Name);
                }
            }

            obj.SetActive(false);

            return obj;
        }

        /// <summary>
        /// 补丁 Pin Projectile FSM
        /// 添加 DIRECT_FIRE 全局转换和 PinArray 大招状态链
        /// </summary>
        private void PatchPinFsm(GameObject pinObj)
        {
            var fsm = pinObj.LocateMyFSM("Control");
            if (fsm == null)
            {
                Log.Warn("[FWPinManager] Pin Projectile 未找到 Control FSM，无法补丁");
                return;
            }

            // 1. 注册所有需要的事件
            var directFireEvent = GetOrCreateEvent(fsm, "DIRECT_FIRE");
            var pinArraySlamEvent = GetOrCreateEvent(fsm, "PINARRAY_SLAM");
            var pinArrayLiftEvent = GetOrCreateEvent(fsm, "PINARRAY_LIFT");
            var pinArrayLandEvent = GetOrCreateEvent(fsm, "PINARRAY_LAND");
            var attackEvent = FsmEvent.GetFsmEvent("ATTACK");
            var isGroundEvent = GetOrCreateEvent(fsm, "IS_GROUND");
            var isAirEvent = GetOrCreateEvent(fsm, "IS_AIR");

            // 1.1 注册 ClimbPin 相关事件
            var climbPinPrepareEvent = GetOrCreateEvent(fsm, "CLIMB_PIN_PREPARE");
            var climbPinAimEvent = GetOrCreateEvent(fsm, "CLIMB_PIN_AIM");
            var climbPinThreadEvent = GetOrCreateEvent(fsm, "CLIMB_PIN_THREAD");  // 新增：触发 Thread 动画
            var climbPinFireEvent = GetOrCreateEvent(fsm, "CLIMB_PIN_FIRE");
            var climbPinTimeoutEvent = GetOrCreateEvent(fsm, "CLIMB_PIN_TIMEOUT");

            // 2. 添加 FSM 变量（用于 Lift 和 Scramble）
            AddPinArrayFsmVariables(fsm);

            // 3. 查找原版状态
            var anticState = FindState(fsm, "Antic");
            var releaseState = FindState(fsm, "Release Pin");
            var attackPauseState = FindState(fsm, "Attack Pause");
            var managedDormantState = FindState(fsm, "Managed Dormant");
            if (anticState == null)
            {
                Log.Warn("[FWPinManager] Pin FSM 未找到 Antic 状态");
                return;
            }

            // 3.1 创建一个“受控休眠”状态，避免原版 Dormant 的 Wait 自动触发 ATTACK 干扰 PinArray 展开期
            if (managedDormantState == null)
            {
                managedDormantState = CreateAndAddState(fsm, "Managed Dormant", "Managed dormant (no auto attack)");
                managedDormantState.Actions = new FsmStateAction[]
                {
                    new SetMeshRenderer
                    {
                        gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                        active = new FsmBool(false)
                    },
                    new SetMeshRenderer
                    {
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            GameObject = new FsmGameObject { Value = GetThreadGameObject(fsm) }
                        },
                        active = new FsmBool(false)
                    }
                };

                if (attackPauseState != null)
                {
                    managedDormantState.Transitions = new FsmTransition[]
                    {
                        CreateTransition(attackEvent, attackPauseState)
                    };
                }
            }

            // 4. 创建 PinArray 大招状态链
            var pinArraySlamPrepareState = CreateAndAddState(fsm, "PinArray Slam Prepare", "PinArray 砸地预备");
            var pinArraySlamPreFireState = CreateAndAddState(fsm, "PinArray Slam PreFire", "PinArray 砸地发射前");
            var pinArraySlamFireState = CreateAndAddState(fsm, "PinArray Slam Fire", "PinArray 砸地发射");
            var pinArraySlamThunkState = CreateAndAddState(fsm, "PinArray Slam Thunk", "PinArray 砸地落地");
            var pinArraySlamRecoverState = CreateAndAddState(fsm, "PinArray Slam Recover", "PinArray 砸地恢复");
            var pinArrayLiftState = CreateAndAddState(fsm, "PinArray Lift", "PinArray 抬起");
            var pinArrayPostLiftState = CreateAndAddState(fsm, "PinArray PostLift", "PinArray 抬起后处理");
            // 打乱角度拆分为多个状态
            var pinArrayScrambleCheckState = CreateAndAddState(fsm, "PinArray Scramble Check", "PinArray 检查位置");
            var pinArrayScrambleGroundState = CreateAndAddState(fsm, "PinArray Scramble Ground", "PinArray 地面基准角度");
            var pinArrayScrambleAirState = CreateAndAddState(fsm, "PinArray Scramble Air", "PinArray 空中基准角度");
            var pinArrayScrambleCalcState = CreateAndAddState(fsm, "PinArray Scramble Calc", "PinArray 计算并旋转");
            var pinArrayReadyState = CreateAndAddState(fsm, "PinArray Ready", "PinArray 等待攻击");

            // 5. 添加状态动作
            AddPinArraySlamPrepareActions(fsm, pinArraySlamPrepareState);
            AddPinArraySlamPreFireActions(fsm, pinArraySlamPreFireState);
            AddPinArraySlamFireActions(fsm, pinArraySlamFireState, pinArrayLandEvent);
            AddPinArraySlamThunkActions(fsm, pinArraySlamThunkState);
            AddPinArraySlamRecoverActions(fsm, pinArraySlamRecoverState);
            AddPinArrayLiftActions(fsm, pinArrayLiftState, pinArrayLiftEvent);
            AddPinArrayPostLiftActions(fsm, pinArrayPostLiftState);
            // 打乱角度状态链
            AddPinArrayScrambleCheckActions(fsm, pinArrayScrambleCheckState, isGroundEvent, isAirEvent);
            AddPinArrayScrambleGroundActions(fsm, pinArrayScrambleGroundState);
            AddPinArrayScrambleAirActions(fsm, pinArrayScrambleAirState);
            AddPinArrayScrambleCalcActions(fsm, pinArrayScrambleCalcState);
            AddPinArrayReadyActions(fsm, pinArrayReadyState);

            // 6. 设置状态转换
            // PinArray Slam Prepare → (FINISHED) → PinArray Slam PreFire
            SetFinishedTransition(pinArraySlamPrepareState, pinArraySlamPreFireState);
            // PinArray Slam PreFire → (FINISHED) → PinArray Slam Fire
            SetFinishedTransition(pinArraySlamPreFireState, pinArraySlamFireState);
            // PinArray Slam Fire → (PINARRAY_LAND) → PinArray Slam Thunk
            pinArraySlamFireState.Transitions = new FsmTransition[]
            {
                CreateTransition(pinArrayLandEvent, pinArraySlamThunkState)
            };
            // PinArray Slam Thunk → (FINISHED) → PinArray Slam Recover
            SetFinishedTransition(pinArraySlamThunkState, pinArraySlamRecoverState);
            // PinArray Slam Recover → (FINISHED) → PinArray Lift
            SetFinishedTransition(pinArraySlamRecoverState, pinArrayLiftState);
            // PinArray Lift 状态结束时触发 PINARRAY_LIFT（由全局转换进入 PinArray PostLift）
            // PinArray PostLift → (FINISHED) → PinArray Scramble Check
            SetFinishedTransition(pinArrayPostLiftState, pinArrayScrambleCheckState);
            // PinArray Scramble Check → (IS_GROUND) → PinArray Scramble Ground
            //                        → (IS_AIR) → PinArray Scramble Air
            pinArrayScrambleCheckState.Transitions = new FsmTransition[]
            {
                CreateTransition(isGroundEvent, pinArrayScrambleGroundState),
                CreateTransition(isAirEvent, pinArrayScrambleAirState)
            };
            // PinArray Scramble Ground → (FINISHED) → PinArray Scramble Calc
            SetFinishedTransition(pinArrayScrambleGroundState, pinArrayScrambleCalcState);
            // PinArray Scramble Air → (FINISHED) → PinArray Scramble Calc
            SetFinishedTransition(pinArrayScrambleAirState, pinArrayScrambleCalcState);
            // PinArray Scramble Calc → (FINISHED) → PinArray Ready
            SetFinishedTransition(pinArrayScrambleCalcState, pinArrayReadyState);
            // PinArray Ready → (ATTACK) → Antic（进入原版发射链）
            pinArrayReadyState.Transitions = new FsmTransition[]
            {
                CreateTransition(attackEvent, anticState)
            };

            // 4.1 创建 ClimbPin 状态链
            var climbPinPrepareState = CreateAndAddState(fsm, "ClimbPin Prepare", "ClimbPin 准备阶段");
            var climbPinFollowState = CreateAndAddState(fsm, "ClimbPin Follow", "ClimbPin 跟随阶段");
            var climbPinAimState = CreateAndAddState(fsm, "ClimbPin Aim", "ClimbPin 瞄准阶段");
            var climbPinAnticState = CreateAndAddState(fsm, "ClimbPin Antic", "ClimbPin 预备动画");
            var climbPinWaitThreadState = CreateAndAddState(fsm, "ClimbPin WaitThread", "ClimbPin 等待发射命令");  // 新增
            var climbPinThreadPullState = CreateAndAddState(fsm, "ClimbPin Thread Pull", "ClimbPin 丝线拉动");
            var climbPinFireState = CreateAndAddState(fsm, "ClimbPin Fire", "ClimbPin 发射阶段");
            var climbPinRecycleState = CreateAndAddState(fsm, "ClimbPin Recycle", "ClimbPin 回收阶段");

            // 5.1 添加 ClimbPin 状态动作
            AddClimbPinPrepareActions(fsm, climbPinPrepareState);
            AddClimbPinFollowActions(fsm, climbPinFollowState);
            AddClimbPinAimActions(fsm, climbPinAimState);
            AddClimbPinAnticActions(fsm, climbPinAnticState);
            AddClimbPinWaitThreadActions(fsm, climbPinWaitThreadState);  // 新增
            AddClimbPinThreadPullActions(fsm, climbPinThreadPullState, climbPinFireEvent);
            AddClimbPinFireActions(fsm, climbPinFireState, climbPinTimeoutEvent, pinObj);
            AddClimbPinRecycleActions(fsm, climbPinRecycleState, pinObj);

            // 6.1 设置 ClimbPin 状态转换
            // ClimbPin Prepare → (FINISHED) → ClimbPin Follow
            SetFinishedTransition(climbPinPrepareState, climbPinFollowState);
            // ClimbPin Follow 等待 CLIMB_PIN_AIM 事件（由全局转换处理）
            // ClimbPin Aim → (FINISHED) → ClimbPin Antic
            SetFinishedTransition(climbPinAimState, climbPinAnticState);
            // ClimbPin Antic → (FINISHED) → ClimbPin WaitThread（等待协程发送 CLIMB_PIN_THREAD）
            SetFinishedTransition(climbPinAnticState, climbPinWaitThreadState);
            // ClimbPin WaitThread → (CLIMB_PIN_THREAD) → ClimbPin Thread Pull
            climbPinWaitThreadState.Transitions = new FsmTransition[]
            {
                CreateTransition(climbPinThreadEvent, climbPinThreadPullState)
            };
            // ClimbPin Thread Pull → (CLIMB_PIN_FIRE) → ClimbPin Fire（由 Wait 动作触发）
            climbPinThreadPullState.Transitions = new FsmTransition[]
            {
                CreateTransition(climbPinFireEvent, climbPinFireState)
            };
            // ClimbPin Fire → (CLIMB_PIN_TIMEOUT) → ClimbPin Recycle
            climbPinFireState.Transitions = new FsmTransition[]
            {
                CreateTransition(climbPinTimeoutEvent, climbPinRecycleState)
            };
            // ClimbPin Recycle → (FINISHED) → Managed Dormant
            SetFinishedTransition(climbPinRecycleState, managedDormantState);

            // 7. 添加全局转换
            var globalTransitions = fsm.Fsm.globalTransitions.ToList();
            globalTransitions.Add(CreateTransition(directFireEvent, anticState));
            globalTransitions.Add(CreateTransition(pinArraySlamEvent, pinArraySlamPrepareState));
            globalTransitions.Add(CreateTransition(pinArrayLiftEvent, pinArrayPostLiftState));
            // ClimbPin 全局转换（只保留 CLIMB_PIN_AIM 一个全局转换）
            globalTransitions.Add(CreateTransition(climbPinPrepareEvent, climbPinPrepareState));
            globalTransitions.Add(CreateTransition(climbPinAimEvent, climbPinAimState));
            fsm.Fsm.globalTransitions = globalTransitions.ToArray();
            // 8. 在 Release Pin 状态末尾添加回收动作
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

            // 9. 初始化 FSM 数据
            fsm.Fsm.InitData();

            Log.Debug($"[FWPinManager] Pin FSM 已补丁，添加 DIRECT_FIRE/PINARRAY_SLAM/PINARRAY_LIFT/CLIMB_PIN 全局转换");
        }

        #region PinArray FSM 变量
        /// <summary>
        /// 添加 PinArray 大招需要的 FSM 变量
        /// </summary>
        private void AddPinArrayFsmVariables(PlayMakerFSM fsm)
        {
            var variables = fsm.FsmVariables;
            var floatVars = variables.FloatVariables.ToList();

            // Lift 相关变量
            if (!floatVars.Any(v => v.Name == "CurrentY"))
            {
                floatVars.Add(new FsmFloat("CurrentY") { Value = 0f });
            }
            if (!floatVars.Any(v => v.Name == "LiftDistance"))
            {
                floatVars.Add(new FsmFloat("LiftDistance") { Value = 0.2f });
            }
            if (!floatVars.Any(v => v.Name == "TargetY"))
            {
                floatVars.Add(new FsmFloat("TargetY") { Value = 0f });
            }

            // Scramble 相关变量
            if (!floatVars.Any(v => v.Name == "CenterY"))
            {
                floatVars.Add(new FsmFloat("CenterY") { Value = 146f });  // 139 + 7
            }
            if (!floatVars.Any(v => v.Name == "BaseAngle"))
            {
                floatVars.Add(new FsmFloat("BaseAngle") { Value = 0f });
            }
            if (!floatVars.Any(v => v.Name == "RandomOffset"))
            {
                floatVars.Add(new FsmFloat("RandomOffset") { Value = 0f });
            }
            if (!floatVars.Any(v => v.Name == "TargetAngle"))
            {
                floatVars.Add(new FsmFloat("TargetAngle") { Value = 0f });
            }

            // ClimbPin 相关变量
            if (!floatVars.Any(v => v.Name == "ClimbPinTargetX"))
            {
                floatVars.Add(new FsmFloat("ClimbPinTargetX") { Value = 0f });
            }
            if (!floatVars.Any(v => v.Name == "ClimbPinTargetY"))
            {
                floatVars.Add(new FsmFloat("ClimbPinTargetY") { Value = 0f });
            }
            if (!floatVars.Any(v => v.Name == "ClimbPinAimAngle"))
            {
                floatVars.Add(new FsmFloat("ClimbPinAimAngle") { Value = -90f });
            }
            // ClimbPin 瞄准偏移方向（-1 = 左，+1 = 右）
            if (!floatVars.Any(v => v.Name == "ClimbPinAimOffsetDirection"))
            {
                floatVars.Add(new FsmFloat("ClimbPinAimOffsetDirection") { Value = -1f });
            }
            // ClimbPin 瞄准偏移距离
            if (!floatVars.Any(v => v.Name == "ClimbPinAimOffset"))
            {
                floatVars.Add(new FsmFloat("ClimbPinAimOffset") { Value = 0.3f });
            }

            variables.FloatVariables = floatVars.ToArray();

            // 重新初始化变量
            variables.Init();

            Log.Debug("[FWPinManager] 已添加 PinArray FSM 变量");
        }

        /// <summary>
        /// 获取 FSM 中的 Float 变量引用
        /// </summary>
        private FsmFloat GetFsmFloatVariable(PlayMakerFSM fsm, string name)
        {
            var variable = fsm.FsmVariables.FindFsmFloat(name);
            if (variable == null)
            {
                Log.Warn($"[FWPinManager] 未找到 FSM 变量: {name}");
                return new FsmFloat(name) { Value = 0f };
            }
            return variable;
        }

        /// <summary>
        /// 获取 FSM 中的 GameObject 变量引用
        /// </summary>
        private FsmGameObject GetFsmGameObjectVariable(PlayMakerFSM fsm, string name)
        {
            var variable = fsm.FsmVariables.FindFsmGameObject(name);
            if (variable == null)
            {
                Log.Warn($"[FWPinManager] 未找到 FSM GameObject 变量: {name}");
                return new FsmGameObject(name) { Value = null };
            }
            return variable;
        }

        /// <summary>
        /// 获取 FSM 中 Thread 子对象的 GameObject（使用 FSM 内部变量）
        /// </summary>
        private GameObject? GetThreadGameObject(PlayMakerFSM fsm)
        {
            var threadVar = fsm.FsmVariables.FindFsmGameObject("Thread");
            return threadVar?.Value;
        }

        /// <summary>
        /// 获取 FSM 中 Damager 子对象的 GameObject（使用 FSM 内部变量）
        /// </summary>
        private GameObject? GetDamagerGameObject(PlayMakerFSM fsm)
        {
            var damagerVar = fsm.FsmVariables.FindFsmGameObject("Damager");
            return damagerVar?.Value;
        }

        /// <summary>
        /// 重置 Pin FSM 变量到初始值
        /// </summary>
        private void ResetPinFsmVariables(PlayMakerFSM fsm)
        {
            var variables = fsm.FsmVariables;

            // 重置 Lift 相关变量
            var currentY = variables.FindFsmFloat("CurrentY");
            if (currentY != null) currentY.Value = 0f;

            var liftDistance = variables.FindFsmFloat("LiftDistance");
            if (liftDistance != null) liftDistance.Value = 0.2f;

            var targetY = variables.FindFsmFloat("TargetY");
            if (targetY != null) targetY.Value = 0f;

            // 重置 Scramble 相关变量
            var centerY = variables.FindFsmFloat("CenterY");
            if (centerY != null) centerY.Value = 146f;

            var baseAngle = variables.FindFsmFloat("BaseAngle");
            if (baseAngle != null) baseAngle.Value = 0f;

            var randomOffset = variables.FindFsmFloat("RandomOffset");
            if (randomOffset != null) randomOffset.Value = 0f;

            var targetAngle = variables.FindFsmFloat("TargetAngle");
            if (targetAngle != null) targetAngle.Value = 0f;

            // 重置 ClimbPin 相关变量
            var climbPinTargetX = variables.FindFsmFloat("ClimbPinTargetX");
            if (climbPinTargetX != null) climbPinTargetX.Value = 0f;

            var climbPinTargetY = variables.FindFsmFloat("ClimbPinTargetY");
            if (climbPinTargetY != null) climbPinTargetY.Value = 0f;

            var climbPinAimAngle = variables.FindFsmFloat("ClimbPinAimAngle");
            if (climbPinAimAngle != null) climbPinAimAngle.Value = -90f;

            var climbPinAimOffsetDirection = variables.FindFsmFloat("ClimbPinAimOffsetDirection");
            if (climbPinAimOffsetDirection != null) climbPinAimOffsetDirection.Value = -1f;

            var climbPinAimOffset = variables.FindFsmFloat("ClimbPinAimOffset");
            if (climbPinAimOffset != null) climbPinAimOffset.Value = 0.3f;
        }
        #endregion

        #region PinArray 状态动作
        /// <summary>
        /// PinArray Slam Prepare 状态动作（砸地前预备：短暂等待准备）
        /// 注意：Thread 显示和动画播放已移到 PreFire 状态
        /// </summary>
        private void AddPinArraySlamPrepareActions(PlayMakerFSM fsm, FsmState state)
        {
            var actions = new List<FsmStateAction>();

            // 短暂等待，让 Pin 完成初始化
            actions.Add(new SetMeshRenderer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                active = new FsmBool(true)
            });
            actions.Add(new Wait
            {
                time = new FsmFloat(0.15f),
                finishEvent = FsmEvent.Finished
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// PinArray Slam PreFire 状态动作（发射前准备：显示Thread、播放音频、播放动画、设置帧、等待0.2s）
        /// </summary>
        private void AddPinArraySlamPreFireActions(PlayMakerFSM fsm, FsmState state)
        {
            var threadObj = GetThreadGameObject(fsm);
            var actions = new List<FsmStateAction>();

            // 1. 播放音频（从 Antic 状态复制）
            var anticState = fsm.FsmStates.FirstOrDefault(s => s.Name == "Antic");
            var playAudioAction = anticState?.Actions.FirstOrDefault(a => a is PlayAudioEvent) as PlayAudioEvent;
            if (playAudioAction != null)
            {
                actions.Add(new PlayAudioEvent
                {
                    Fsm = fsm.Fsm,
                    audioClip = playAudioAction.audioClip,
                    volume = playAudioAction.volume,
                    pitchMin = playAudioAction.pitchMin,
                    pitchMax = playAudioAction.pitchMax,
                    audioPlayerPrefab = playAudioAction.audioPlayerPrefab,
                    spawnPoint = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    spawnPosition = playAudioAction.spawnPosition,
                    SpawnedPlayerRef = playAudioAction.SpawnedPlayerRef
                });
            }

            // 2. 显示 Thread
            if (threadObj != null)
            {
                actions.Add(new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    active = new FsmBool(true)
                });

                // 3. 播放 Pin Thread 动画
                actions.Add(new Tk2dPlayAnimationWithEvents
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    clipName = new FsmString("Pin Thread") { Value = "Pin Thread" },
                    animationCompleteEvent = FsmEvent.Finished
                });

                // 4. 设置 Thread 从第 0 帧开始播放
                actions.Add(new Tk2dPlayFrame
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    frame = new FsmInt(0)
                });
            }

            // 5. 等待 0.55s 后进入 Fire
            actions.Add(new Wait
            {
                time = new FsmFloat(0.55f),
                finishEvent = FsmEvent.Finished
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// PinArray Slam Fire 状态动作（简化版：向下砸地）
        /// </summary>
        private void AddPinArraySlamFireActions(PlayMakerFSM fsm, FsmState state, FsmEvent landEvent)
        {
            // 获取子对象引用（使用 FSM 内部变量）
            var threadObj = GetThreadGameObject(fsm);
            var damagerObj = GetDamagerGameObject(fsm);

            var actions = new List<FsmStateAction>();

            // 隐藏 Thread
            if (threadObj != null)
            {
                actions.Add(new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    active = new FsmBool(false)
                });
            }

            // 播放 Pin Fire 动画
            actions.Add(new Tk2dPlayAnimation
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animLibName = new FsmString("") { Value = "" },
                clipName = new FsmString("Pin Fire") { Value = "Pin Fire" }
            });

            // 注意：音频播放已移到 PreFire 状态

            // 关闭 Kinematic（先关闭才能受物理影响）
            actions.Add(new SetIsKinematic2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                isKinematic = new FsmBool(false)
            });

            // 设置速度（向下，速度 180）
            actions.Add(new SetVelocityAsAngle
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                angle = new FsmFloat(-90f),  // 向下
                speed = new FsmFloat(180f),
                everyFrame = false
            });

            // 碰撞检测 - OnCollisionEnter2D
            actions.Add(new Collision2dEvent
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                collision = Collision2DType.OnCollisionEnter2D,
                collideTag = new FsmString("") { Value = "" },
                sendEvent = landEvent,
                storeCollider = new FsmGameObject { Value = null },
                storeForce = new FsmFloat { Value = 0f }
            });

            // 碰撞检测 - OnCollisionStay2D
            actions.Add(new Collision2dEvent
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                collision = Collision2DType.OnCollisionStay2D,
                collideTag = new FsmString("") { Value = "" },
                sendEvent = landEvent,
                storeCollider = new FsmGameObject { Value = null },
                storeForce = new FsmFloat { Value = 0f }
            });

            // 激活 Damager
            if (damagerObj != null)
            {
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = damagerObj }
                    },
                    activate = new FsmBool(true),
                    recursive = new FsmBool(false),
                    resetOnExit = true
                });
            }

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// PinArray Slam Thunk 状态动作（复制原版 Thunk）
        /// </summary>
        private void AddPinArraySlamThunkActions(PlayMakerFSM fsm, FsmState state)
        {
            var cameraShake = new DoCameraShake();
            cameraShake.Reset();
            cameraShake.VisibleRenderer = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner };
            cameraShake.Profile = new FsmObject
            {
                Value = UnityEngine.Resources.FindObjectsOfTypeAll<CameraShakeProfile>()
                    .FirstOrDefault(p => p.name == "Small Shake")
            };
            cameraShake.Delay = new FsmFloat(0f);

            state.Actions = new FsmStateAction[]
            {
                // 调用 ResetPinVisualState 设置 Pin Antic 最后一帧（不播放动画）
                new CallMethod
                {
                    behaviour = this,
                    methodName = "ResetPinVisualState",
                    parameters = new[]
                    {
                        new FsmVar
                        {
                            type = VariableType.GameObject,
                            objectReference = fsm.gameObject
                        }
                    },
                    everyFrame = false
                },
                // 清零速度
                new SetVelocity2d
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    vector = new FsmVector2 { Value = Vector2.zero, UseVariable = false },
                    x = new FsmFloat { Value = 0f, UseVariable = false },
                    y = new FsmFloat { Value = 0f, UseVariable = false },
                    everyFrame = false
                },
                // 震屏
                cameraShake,
                // 开启 Kinematic
                new SetIsKinematic2d
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    isKinematic = new FsmBool(true)
                },
                // 短暂等待后跳转
                new Wait
                {
                    time = new FsmFloat(0.1f),
                    finishEvent = FsmEvent.Finished
                }
            };
        }

        /// <summary>
        /// PinArray Slam Recover 状态动作（恢复参数）
        /// </summary>
        private void AddPinArraySlamRecoverActions(PlayMakerFSM fsm, FsmState state)
        {
            var damagerObj = GetDamagerGameObject(fsm);
            var actions = new List<FsmStateAction>();

            // 关闭 Damager
            if (damagerObj != null)
            {
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = damagerObj }
                    },
                    activate = new FsmBool(false),
                    recursive = new FsmBool(false),
                    resetOnExit = false
                });
            }

            // 清零速度
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = Vector2.zero, UseVariable = false },
                x = new FsmFloat { Value = 0f, UseVariable = false },
                y = new FsmFloat { Value = 0f, UseVariable = false },
                everyFrame = false
            });

            // 短暂等待
            actions.Add(new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// PinArray Lift 状态动作（向上抬起，使用 GetPosition + FloatAdd + AnimateYPositionTo）
        /// </summary>
        private void AddPinArrayLiftActions(PlayMakerFSM fsm, FsmState state, FsmEvent liftEvent)
        {
            // 获取 FSM 变量引用
            var currentY = GetFsmFloatVariable(fsm, "CurrentY");
            var liftDistance = GetFsmFloatVariable(fsm, "LiftDistance");
            var targetY = GetFsmFloatVariable(fsm, "TargetY");

            state.Actions = new FsmStateAction[]
            {
                // 1. 获取当前 Y 位置
                new GetPosition
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    vector = new FsmVector3 { UseVariable = true },
                    x = new FsmFloat { UseVariable = true },
                    y = currentY,
                    z = new FsmFloat { UseVariable = true },
                    space = Space.World,
                    everyFrame = false
                },
                // 2. 计算目标 Y = CurrentY + LiftDistance
                new SetFloatValue
                {
                    floatVariable = targetY,
                    floatValue = currentY,
                    everyFrame = false
                },
                new FloatAdd
                {
                    floatVariable = targetY,
                    add = liftDistance,
                    everyFrame = false,
                    perSecond = false
                },
                // 3. 平滑移动到目标 Y
                new AnimateYPositionTo
                {
                    GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    ToValue = targetY,
                    localSpace = false,
                    time = new FsmFloat(0.35f),
                    delay = new FsmFloat { UseVariable = true },
                    speed = new FsmFloat { UseVariable = true },
                    reverse = new FsmBool(false),
                    easeType = EaseFsmAction.EaseType.easeInOutSine,
                    finishEvent = liftEvent
                }
            };
        }

        private void AddPinArrayPostLiftActions(PlayMakerFSM fsm, FsmState state)
        {
            state.Actions = new FsmStateAction[]
            {
                // 显示 MeshRenderer
                new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    active = new FsmBool(true)
                },
                // 开启 Kinematic
                new SetIsKinematic2d
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    isKinematic = new FsmBool(true)
                },
                new Wait
                {
                    time = new FsmFloat(0.1f),
                    finishEvent = FsmEvent.Finished
                }
            };
        }

        /// <summary>
        /// PinArray Scramble Check 状态动作（检查 Y 位置判断地面/空中）
        /// </summary>
        private void AddPinArrayScrambleCheckActions(PlayMakerFSM fsm, FsmState state, FsmEvent isGroundEvent, FsmEvent isAirEvent)
        {
            var currentY = GetFsmFloatVariable(fsm, "CurrentY");
            var centerY = GetFsmFloatVariable(fsm, "CenterY");

            state.Actions = new FsmStateAction[]
            {
                // 1. 获取当前 Y 位置
                new GetPosition
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    vector = new FsmVector3 { UseVariable = true },
                    x = new FsmFloat { UseVariable = true },
                    y = currentY,
                    z = new FsmFloat { UseVariable = true },
                    space = Space.World,
                    everyFrame = false
                },
                // 2. 比较 Y 与 CenterY，决定是地面还是空中
                new FloatCompare
                {
                    float1 = currentY,
                    float2 = centerY,
                    tolerance = new FsmFloat(1f),
                    equal = isAirEvent,
                    lessThan = isGroundEvent,    // Y < CenterY → 地面
                    greaterThan = isAirEvent,    // Y > CenterY → 空中
                    everyFrame = false
                }
            };
        }

        /// <summary>
        /// PinArray Scramble Ground 状态动作（设置地面基准角度 90° 朝上）
        /// </summary>
        private void AddPinArrayScrambleGroundActions(PlayMakerFSM fsm, FsmState state)
        {
            var baseAngle = GetFsmFloatVariable(fsm, "BaseAngle");

            state.Actions = new FsmStateAction[]
            {
                new SetFloatValue
                {
                    floatVariable = baseAngle,
                    floatValue = new FsmFloat(90f),  // 地面的针朝上
                    everyFrame = false
                }
            };
        }

        /// <summary>
        /// PinArray Scramble Air 状态动作（设置空中基准角度 -90° 朝下）
        /// </summary>
        private void AddPinArrayScrambleAirActions(PlayMakerFSM fsm, FsmState state)
        {
            var baseAngle = GetFsmFloatVariable(fsm, "BaseAngle");

            state.Actions = new FsmStateAction[]
            {
                new SetFloatValue
                {
                    floatVariable = baseAngle,
                    floatValue = new FsmFloat(-90f),  // 空中的针朝下
                    everyFrame = false
                }
            };
        }

        /// <summary>
        /// PinArray Scramble Calc 状态动作（计算随机角度并旋转）
        /// </summary>
        private void AddPinArrayScrambleCalcActions(PlayMakerFSM fsm, FsmState state)
        {
            var baseAngle = GetFsmFloatVariable(fsm, "BaseAngle");
            var randomOffset = GetFsmFloatVariable(fsm, "RandomOffset");
            var targetAngle = GetFsmFloatVariable(fsm, "TargetAngle");

            state.Actions = new FsmStateAction[]
            {
                // 1. 生成随机偏移 ±45°
                new RandomFloat
                {
                    min = new FsmFloat(-45f),
                    max = new FsmFloat(45f),
                    storeResult = randomOffset
                },
                // 2. 计算目标角度 = BaseAngle + RandomOffset
                new SetFloatValue
                {
                    floatVariable = targetAngle,
                    floatValue = baseAngle,
                    everyFrame = false
                },
                new FloatAdd
                {
                    floatVariable = targetAngle,
                    add = randomOffset,
                    everyFrame = false,
                    perSecond = false
                },
                // 2.1 将目标角度归一到“从当前角度出发的最短路径”，避免 AnimateRotationTo 走远路（例如 40° 变 320°）
                new CallMethod
                {
                    behaviour = this,
                    methodName = "NormalizePinTargetAngle",
                    parameters = new[]
                    {
                        new FsmVar
                        {
                            type = VariableType.GameObject,
                            objectReference = fsm.gameObject
                        }
                    },
                    everyFrame = false
                },
                // 3. 平滑旋转到目标角度
                new AnimateRotationTo
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fromValue = new FsmFloat { UseVariable = true },  // 使用当前角度
                    toValue = targetAngle,
                    worldSpace = false,
                    negativeSpace = false,
                    time = new FsmFloat(0.15f),
                    delay = new FsmFloat { UseVariable = true },
                    speed = new FsmFloat { UseVariable = true },
                    reverse = new FsmBool(false),
                    easeType = EaseFsmAction.EaseType.easeOutQuad,
                    finishEvent = FsmEvent.Finished
                }
            };
        }

        /// <summary>
        /// PinArray Ready 状态动作（等待 ATTACK）
        /// </summary>
        private void AddPinArrayReadyActions(PlayMakerFSM fsm, FsmState state)
        {
            state.Actions = new FsmStateAction[]
            {
                // 播放 Pin Antic 动画（准备姿态）
                new CallMethod
                {
                    behaviour = this,
                    methodName = "ResetPinVisualState",
                    parameters = new[]
                    {
                        new FsmVar
                        {
                            type = VariableType.GameObject,
                            objectReference = fsm.gameObject
                        }
                    },
                    everyFrame = false
                }
                // 不设置 Wait，持续等待 ATTACK 事件
            };
        }

        #region ClimbPin 状态动作
        /// <summary>
        /// ClimbPin Prepare 状态动作（准备阶段：设置层级、显示 Pin，设置初始朝向，播放 Pin Antic 最后一帧）
        /// </summary>
        private void AddClimbPinPrepareActions(PlayMakerFSM fsm, FsmState state)
        {
            var actions = new List<FsmStateAction>();

            // 1. 设置层级为 Ignore Raycast（可穿墙）
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = LayerMask.NameToLayer("Ignore Raycast")
            });

            // 2. 显示 MeshRenderer
            actions.Add(new SetMeshRenderer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                active = new FsmBool(true)
            });

            // 3. 设置初始朝向（-90° 向下）
            actions.Add(new SetRotation
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                quaternion = new FsmQuaternion { UseVariable = true },
                vector = new FsmVector3 { Value = new Vector3(0, 0, -90f), UseVariable = false },
                xAngle = new FsmFloat { UseVariable = true },
                yAngle = new FsmFloat { UseVariable = true },
                zAngle = new FsmFloat { Value = -90f, UseVariable = false },
                space = Space.World,
                everyFrame = false,
                lateUpdate = false
            });

            // 4. 播放 Pin Antic 最后一帧（通过 CallMethod 调用 ResetPinVisualState）
            actions.Add(new CallMethod
            {
                behaviour = this,
                methodName = "ResetPinVisualState",
                parameters = new[]
                {
                    new FsmVar
                    {
                        type = VariableType.GameObject,
                        objectReference = fsm.gameObject
                    }
                },
                everyFrame = false
            });

            // 5. 短暂等待后进入 Follow 状态
            actions.Add(new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// ClimbPin Follow 状态动作（跟随阶段：等待 CLIMB_PIN_AIM 事件，由协程控制位置）
        /// </summary>
        private void AddClimbPinFollowActions(PlayMakerFSM fsm, FsmState state)
        {
            // 跟随阶段不需要 FSM 动作，位置由协程控制
            // 只需要等待 CLIMB_PIN_AIM 事件（通过全局转换处理）
            state.Actions = new FsmStateAction[] { };
        }

        /// <summary>
        /// ClimbPin Aim 状态动作（瞄准阶段：先计算瞄准角度，再旋转指向目标点）
        /// </summary>
        private void AddClimbPinAimActions(PlayMakerFSM fsm, FsmState state)
        {
            var climbPinAimAngle = GetFsmFloatVariable(fsm, "ClimbPinAimAngle");

            var actions = new List<FsmStateAction>();

            // 1. 先调用 UpdateClimbPinAim 计算正确的瞄准角度（根据偏移方向）
            actions.Add(new CallMethod
            {
                behaviour = this,
                methodName = "UpdateClimbPinAim",
                parameters = new[]
                {
                    new FsmVar
                    {
                        type = VariableType.GameObject,
                        objectReference = fsm.gameObject
                    }
                },
                everyFrame = false  // 只调用一次
            });

            // 2. 平滑旋转到目标角度
            actions.Add(new AnimateRotationTo
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fromValue = new FsmFloat { UseVariable = true },  // 使用当前角度
                toValue = climbPinAimAngle,
                worldSpace = false,
                negativeSpace = false,
                time = new FsmFloat(0.2f),
                delay = new FsmFloat { UseVariable = true },
                speed = new FsmFloat { UseVariable = true },
                reverse = new FsmBool(false),
                easeType = EaseFsmAction.EaseType.easeOutQuad,
                finishEvent = FsmEvent.Finished
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// ClimbPin Antic 状态动作（克隆原版 Antic：SetMeshRenderer、Tk2dPlayAnimationWithEvents、PlayAudioEvent）
        /// </summary>
        private void AddClimbPinAnticActions(PlayMakerFSM fsm, FsmState state)
        {
            var actions = new List<FsmStateAction>();

            // 1. 显示 MeshRenderer
            actions.Add(new SetMeshRenderer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                active = new FsmBool(true)
            });

            // 2. 播放 Pin Antic 动画
            actions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Pin Antic") { Value = "Pin Antic" },
                animationTriggerEvent = FsmEvent.Finished
            });

            // 3. 播放音效（从原版 Antic 状态复制）
            var anticState = fsm.FsmStates.FirstOrDefault(s => s.Name == "Antic");
            var playAudioAction = anticState?.Actions.FirstOrDefault(a => a is PlayAudioEvent) as PlayAudioEvent;
            if (playAudioAction != null)
            {
                actions.Add(new PlayAudioEvent
                {
                    Fsm = fsm.Fsm,
                    audioClip = playAudioAction.audioClip,
                    volume = playAudioAction.volume,
                    pitchMin = playAudioAction.pitchMin,
                    pitchMax = playAudioAction.pitchMax,
                    audioPlayerPrefab = playAudioAction.audioPlayerPrefab,
                    spawnPoint = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    spawnPosition = playAudioAction.spawnPosition,
                    SpawnedPlayerRef = playAudioAction.SpawnedPlayerRef
                });
            }

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// ClimbPin WaitThread 状态动作（等待发射命令：每帧更新瞄准角度，等待协程发送 CLIMB_PIN_THREAD 事件）
        /// </summary>
        private void AddClimbPinWaitThreadActions(PlayMakerFSM fsm, FsmState state)
        {
            var actions = new List<FsmStateAction>();

            // 每帧调用 UpdateClimbPinAim 更新瞄准角度和旋转
            actions.Add(new CallMethod
            {
                behaviour = this,
                methodName = "UpdateClimbPinAim",
                parameters = new[]
                {
                    new FsmVar
                    {
                        type = VariableType.GameObject,
                        objectReference = fsm.gameObject
                    }
                },
                everyFrame = true  // 每帧调用
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// ClimbPin Thread Pull 状态动作（克隆原版 Thread Pull，修改 Wait 的 finishEvent 为 CLIMB_PIN_FIRE）
        /// </summary>
        private void AddClimbPinThreadPullActions(PlayMakerFSM fsm, FsmState state, FsmEvent climbPinFireEvent)
        {
            var threadObj = GetThreadGameObject(fsm);
            var actions = new List<FsmStateAction>();

            // 1. 显示 Thread MeshRenderer
            if (threadObj != null)
            {
                actions.Add(new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    active = new FsmBool(true)
                });

                // 2. 播放 Pin Thread 动画
                actions.Add(new Tk2dPlayAnimationWithEvents
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    clipName = new FsmString("Pin Thread") { Value = "Pin Thread" },
                    animationCompleteEvent = FsmEvent.Finished
                });

                // 3. 设置 Thread 从第 0 帧开始播放
                actions.Add(new Tk2dPlayFrame
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    frame = new FsmInt(0)
                });
            }

            // 4. 等待 0.55 秒后触发 CLIMB_PIN_FIRE（而非原版的 ATTACK）
            actions.Add(new Wait
            {
                time = new FsmFloat(0.55f),
                finishEvent = climbPinFireEvent
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// ClimbPin Fire 状态动作（发射阶段：隐藏 Thread、播放 Pin Fire 动画、SetVelocityAsAngle、激活 Damager、4 秒 Wait）
        /// </summary>
        private void AddClimbPinFireActions(PlayMakerFSM fsm, FsmState state, FsmEvent climbPinTimeoutEvent, GameObject pinObj)
        {
            var threadObj = GetThreadGameObject(fsm);
            var damagerObj = GetDamagerGameObject(fsm);
            var climbPinAimAngle = GetFsmFloatVariable(fsm, "ClimbPinAimAngle");

            var actions = new List<FsmStateAction>();

            // 1. 隐藏 Thread
            if (threadObj != null)
            {
                actions.Add(new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    active = new FsmBool(false)
                });
            }

            // 2. 播放 Pin Fire 动画
            actions.Add(new Tk2dPlayAnimation
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animLibName = new FsmString("") { Value = "" },
                clipName = new FsmString("Pin Fire") { Value = "Pin Fire" }
            });

            // 3. 关闭 Kinematic
            actions.Add(new SetIsKinematic2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                isKinematic = new FsmBool(false)
            });

            // 4. 设置速度（使用 ClimbPinAimAngle 变量存储的瞄准角度）
            actions.Add(new SetVelocityAsAngle
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                angle = climbPinAimAngle,
                speed = new FsmFloat(120f),
                everyFrame = false
            });

            // 5. 激活 Damager
            if (damagerObj != null)
            {
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = damagerObj }
                    },
                    activate = new FsmBool(true),
                    recursive = new FsmBool(false),
                    resetOnExit = true
                });
            }

            // 6. 播放发射音效（从原版 Fire 状态复制）
            var fireState = fsm.FsmStates.FirstOrDefault(s => s.Name == "Fire");
            var playAudioRandomAction = fireState?.Actions.FirstOrDefault(a => a is PlayAudioEventRandom) as PlayAudioEventRandom;
            if (playAudioRandomAction != null)
            {
                actions.Add(new PlayAudioEventRandom
                {
                    Fsm = fsm.Fsm,
                    audioClips = playAudioRandomAction.audioClips,
                    pitchMin = playAudioRandomAction.pitchMin,
                    pitchMax = playAudioRandomAction.pitchMax,
                    volume = playAudioRandomAction.volume,
                    audioPlayerPrefab = playAudioRandomAction.audioPlayerPrefab,
                    spawnPoint = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    spawnPosition = playAudioRandomAction.spawnPosition,
                    SpawnedPlayerRef = playAudioRandomAction.SpawnedPlayerRef
                });
            }

            // 7. 等待 4 秒后触发超时（不监听碰撞）
            actions.Add(new Wait
            {
                time = new FsmFloat(4f),
                finishEvent = climbPinTimeoutEvent
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// ClimbPin Recycle 状态动作（回收阶段：恢复层级、隐藏 MeshRenderer、重置位置、关闭 Damager、调用 RecyclePinProjectile）
        /// </summary>
        private void AddClimbPinRecycleActions(PlayMakerFSM fsm, FsmState state, GameObject pinObj)
        {
            var threadObj = GetThreadGameObject(fsm);
            var damagerObj = GetDamagerGameObject(fsm);

            var actions = new List<FsmStateAction>();

            // 1. 恢复层级为 Terrain Detector
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = LayerMask.NameToLayer("Terrain Detector")
            });

            // 2. 隐藏主体 MeshRenderer
            actions.Add(new SetMeshRenderer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                active = new FsmBool(false)
            });

            // 3. 隐藏 Thread MeshRenderer
            if (threadObj != null)
            {
                actions.Add(new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = threadObj }
                    },
                    active = new FsmBool(false)
                });
            }

            // 4. 清零速度
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = Vector2.zero, UseVariable = false },
                x = new FsmFloat { Value = 0f, UseVariable = false },
                y = new FsmFloat { Value = 0f, UseVariable = false },
                everyFrame = false
            });

            // 5. 开启 Kinematic
            actions.Add(new SetIsKinematic2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                isKinematic = new FsmBool(true)
            });

            // 6. 关闭 Damager
            if (damagerObj != null)
            {
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = damagerObj }
                    },
                    activate = new FsmBool(false),
                    recursive = new FsmBool(false),
                    resetOnExit = false
                });
            }

            // 7. 调用 RecyclePinProjectile 回收到对象池
            actions.Add(new CallMethod
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
            });

            // 8. 触发 FINISHED 进入 Managed Dormant
            actions.Add(new SendEvent
            {
                sendEvent = FsmEvent.Finished,
                delay = new FsmFloat(0.1f),
                everyFrame = false
            });

            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 设置 ClimbPin 的瞄准角度（由协程调用）
        /// </summary>
        public void SetClimbPinAimAngle(GameObject pinObj, float targetX, float targetY)
        {
            if (pinObj == null) return;

            var fsm = pinObj.LocateMyFSM("Control");
            if (fsm == null) return;

            // 计算从 Pin 位置到目标点的角度
            Vector3 pinPos = pinObj.transform.position;
            float dx = targetX - pinPos.x;
            float dy = targetY - pinPos.y;
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            // 设置 FSM 变量
            var climbPinTargetX = fsm.FsmVariables.FindFsmFloat("ClimbPinTargetX");
            if (climbPinTargetX != null) climbPinTargetX.Value = targetX;

            var climbPinTargetY = fsm.FsmVariables.FindFsmFloat("ClimbPinTargetY");
            if (climbPinTargetY != null) climbPinTargetY.Value = targetY;

            var climbPinAimAngle = fsm.FsmVariables.FindFsmFloat("ClimbPinAimAngle");
            if (climbPinAimAngle != null) climbPinAimAngle.Value = angle;
        }

        /// <summary>
        /// 设置 ClimbPin 的瞄准偏移方向和距离（由协程在生成时调用）
        /// </summary>
        /// <param name="pinObj">Pin 对象</param>
        /// <param name="offsetDirection">偏移方向：-1 = 左，+1 = 右</param>
        /// <param name="aimOffset">偏移距离</param>
        public void SetClimbPinAimOffsetDirection(GameObject pinObj, float offsetDirection, float aimOffset)
        {
            if (pinObj == null) return;

            var fsm = pinObj.LocateMyFSM("Control");
            if (fsm == null) return;

            var climbPinAimOffsetDirection = fsm.FsmVariables.FindFsmFloat("ClimbPinAimOffsetDirection");
            if (climbPinAimOffsetDirection != null) climbPinAimOffsetDirection.Value = offsetDirection;

            var climbPinAimOffsetVar = fsm.FsmVariables.FindFsmFloat("ClimbPinAimOffset");
            if (climbPinAimOffsetVar != null) climbPinAimOffsetVar.Value = aimOffset;
        }

        /// <summary>
        /// 每帧更新 ClimbPin 的瞄准角度和旋转（由 WaitThread 状态每帧调用）
        /// </summary>
        public void UpdateClimbPinAim(GameObject pinObj)
        {
            if (pinObj == null) return;

            var hero = HeroController.instance;
            if (hero == null) return;

            var fsm = pinObj.LocateMyFSM("Control");
            if (fsm == null) return;

            // 获取偏移方向和距离
            var climbPinAimOffsetDirection = fsm.FsmVariables.FindFsmFloat("ClimbPinAimOffsetDirection");
            var climbPinAimOffset = fsm.FsmVariables.FindFsmFloat("ClimbPinAimOffset");
            
            float offsetDirection = climbPinAimOffsetDirection?.Value ?? -1f;
            float aimOffset = climbPinAimOffset?.Value ?? 0.3f;

            // 计算目标点（玩家位置 + 偏移）
            Vector3 playerPos = hero.transform.position;
            float targetX = playerPos.x + (aimOffset * offsetDirection);
            float targetY = playerPos.y;

            // 计算从 Pin 位置到目标点的角度
            Vector3 pinPos = pinObj.transform.position;
            float dx = targetX - pinPos.x;
            float dy = targetY - pinPos.y;
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            // 更新 FSM 变量
            var climbPinTargetX = fsm.FsmVariables.FindFsmFloat("ClimbPinTargetX");
            if (climbPinTargetX != null) climbPinTargetX.Value = targetX;

            var climbPinTargetY = fsm.FsmVariables.FindFsmFloat("ClimbPinTargetY");
            if (climbPinTargetY != null) climbPinTargetY.Value = targetY;

            var climbPinAimAngle = fsm.FsmVariables.FindFsmFloat("ClimbPinAimAngle");
            if (climbPinAimAngle != null) climbPinAimAngle.Value = angle;

            // 更新 Pin 的旋转角度
            pinObj.transform.rotation = Quaternion.Euler(0, 0, angle);
        }
        #endregion
        #endregion

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

                // 清理刚体残留速度，避免出池后短时间内与 Transform 驱动的移动/旋转打架
                var rb2d = obj.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    rb2d.linearVelocity = Vector2.zero;
                    rb2d.angularVelocity = 0f;
                    rb2d.bodyType = RigidbodyType2D.Kinematic;
                }

                ResetPinVisualState(obj);

                var fsm = obj.LocateMyFSM("Control");
                if (fsm != null)
                {
                    // 重置 FSM 变量
                    ResetPinFsmVariables(fsm);

                    // 重置 FSM 到初始状态
                    fsm.Fsm.InitData();

                    // 强制进入 Managed Dormant 状态（等待事件触发；不会自动触发 ATTACK）
                    var managedDormantState = FindState(fsm, "Managed Dormant");
                    if (managedDormantState != null)
                    {
                        fsm.SetState(managedDormantState.Name);
                    }
                    else
                    {
                        var dormantState = FindState(fsm, "Dormant");
                        if (dormantState != null)
                        {
                            fsm.SetState(dormantState.Name);
                        }
                    }
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
            obj.SetActive(false);
            Log.Debug($"[FWPinManager] Pin Projectile 已回收，池中数量: {_pinProjectilePool.Count}");
        }

        public void ResetPinVisualState(GameObject pinObj)
        {
            if (pinObj == null) return;

            var animator = pinObj.GetComponent<tk2dSpriteAnimator>();
            if (animator != null)
            {
                var clip = animator.GetClipByName("Pin Antic");
                if (clip != null)
                {
                    animator.Play(clip);
                    if (clip.frames != null && clip.frames.Length > 0)
                    {
                        animator.SetFrame(clip.frames.Length - 1, false);
                    }
                    animator.Stop();
                }
            }
        }

        public void NormalizePinTargetAngle(GameObject pinObj)
        {
            if (pinObj == null) return;

            var fsm = pinObj.LocateMyFSM("Control");
            if (fsm == null) return;

            var targetAngle = fsm.FsmVariables.FindFsmFloat("TargetAngle");
            if (targetAngle == null) return;

            float current = pinObj.transform.localEulerAngles.z;
            float desired = targetAngle.Value;
            float delta = Mathf.DeltaAngle(current, desired);
            targetAngle.Value = current + delta;
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
        public void CleanupPool()
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
