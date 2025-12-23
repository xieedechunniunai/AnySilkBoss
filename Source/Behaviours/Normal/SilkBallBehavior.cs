using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Actions;
using AnySilkBoss.Source.Tools;
using System.Collections.Generic;
using System;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    /// <summary>
    /// 丝球Behavior - 管理单个丝球的行为和FSM
    /// </summary>
    internal class SilkBallBehavior : MonoBehaviour
    {
        #region 物理参数
        [Header("物理参数")]
        public float acceleration = 30f;           // 追踪玩家的加速度
        public float maxSpeed = 20f;               // 最大速度限制
        public float chaseTime = 6f;              // 追逐时长
        public float scale = 1f;                  // 整体缩放

        [Header("自转参数")]
        public bool enableRotation = true;        // 是否启用自转
        public float rotationSpeed = 360f;        // 自转速度（度/秒），正值为逆时针
        #endregion

        #region 状态标记
        [Header("状态标记")]
        public bool isActive = false;             // 是否激活
        public bool isChasing = false;            // 是否正在追踪玩家
        public bool isPrepared = false;           // 是否已准备
        public bool ignoreWallCollision = false;  // 是否忽略墙壁碰撞（用于追踪大丝球的小丝球）
        private bool isProtected = false;         // 是否处于保护时间内（不会因碰到英雄或墙壁消失）
        public bool canBeAbsorbed = false;        // 是否可以被大丝球吸收（仅吸收阶段的小丝球为true）
        private bool _delayDamageActivation = true;

        // 对象池相关
        private bool _isAvailable = true;         // 是否可用（在对象池中）
        private Transform? _poolContainer;        // 对象池容器引用
        #endregion

        #region 组件引用
        private PlayMakerFSM? controlFSM;          // Control FSM
        private Rigidbody2D? rb2d;                 // 刚体组件
        private Transform? playerTransform;        // 玩家Transform
        internal Transform? customTarget;          // 自定义追踪目标（可以是大丝球等）
        private DamageHero? damageHero;            // 伤害组件

        // 管理器引用
        private Managers.SilkBallManager? silkBallManager;  // 丝球管理器
        private DamageHero? originalDamageHero;            // 原始 DamageHero 组件引用

        // 子物体引用
        private Transform? spriteSilk;             // Sprite Silk 子物体
        private Transform? Glow;          // Glow 子物体
        private CircleCollider2D? mainCollider;    // 主碰撞器
        private float _originalColliderRadius;     // 原始碰撞器半径（缩小50%后的值）
        private ParticleSystem? ptCollect;         // 快速消散粒子
        private ParticleSystem? ptDisappear;       // 缓慢消失粒子
        #endregion

        #region FSM 变量引用
        private FsmBool? readyVar;
        private FsmFloat? accelerationVar;
        private FsmFloat? maxSpeedVar;
        private FsmGameObject? targetGameObjectVar;
        #endregion

        #region 事件引用
        private FsmEvent? prepareEvent;
        private FsmEvent? releaseEvent;
        private FsmEvent? hitWallEvent;
        private FsmEvent? hitHeroEvent;
        private FsmEvent? hasGravityEvent;  // 重力状态事件
        #endregion

        #region Properties
        /// <summary>
        /// 是否可用（在对象池中）
        /// </summary>
        public bool IsAvailable => _isAvailable && !isActive;
        #endregion

        private void Awake()
        {
            // 在 Awake 中获取基本组件引用
            GetComponentReferences();
        }

        private void Update()
        {
            // 处理自转
            if (isActive && enableRotation)
            {
                // 逆时针旋转（在 Unity 中，正值的 Z 轴旋转为逆时针）
                transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// 获取组件引用
        /// </summary>
        private void GetComponentReferences()
        {
            // 首先获取 SilkBallManager
            if (silkBallManager == null)
            {
                var managerObj = GameObject.Find("AnySilkBossManager");
                if (managerObj != null)
                {
                    silkBallManager = managerObj.GetComponent<Managers.SilkBallManager>();
                    if (silkBallManager == null)
                    {
                        Log.Warn("AnySilkBossManager 上未找到 SilkBallManager 组件");
                    }
                }
                else
                {
                    Log.Warn("未找到 AnySilkBossManager GameObject");
                }
            }

            // 从 DamageHeroEventManager 获取原始 DamageHero 引用
            if (originalDamageHero == null)
            {
                var managerObj = GameObject.Find("AnySilkBossManager");
                if (managerObj != null)
                {
                    var damageHeroEventManager = managerObj.GetComponent<Managers.DamageHeroEventManager>();
                    if (damageHeroEventManager != null)
                    {
                        if (!damageHeroEventManager.IsInitialized())
                        {
                            Log.Warn("DamageHeroEventManager 尚未初始化，无法获取 DamageHero 引用");
                        }
                        else if (damageHeroEventManager.HasDamageHero())
                        {
                            originalDamageHero = damageHeroEventManager.DamageHero;
                        }
                        else
                        {
                            Log.Error("DamageHeroEventManager 中未找到 DamageHero 组件");
                        }
                    }
                    else
                    {
                        Log.Error("未找到 DamageHeroEventManager 组件");
                    }
                }
            }

            // 获取或添加 Rigidbody2D（只在第一次调用时）
            if (rb2d == null)
            {
                rb2d = GetComponent<Rigidbody2D>();
                if (rb2d == null)
                {
                    rb2d = gameObject.AddComponent<Rigidbody2D>();
                }

                // 配置 Rigidbody2D
                rb2d.gravityScale = 0f;
                rb2d.linearDamping = 0.5f;
                rb2d.bodyType = RigidbodyType2D.Dynamic;
                rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }

            // 获取子物体引用（只在第一次调用时）
            if (spriteSilk == null)
            {
                spriteSilk = transform.Find("Sprite Silk");
                if (spriteSilk != null)
                {
                    mainCollider = spriteSilk.GetComponent<CircleCollider2D>();
                    if (mainCollider != null)
                    {
                        mainCollider.isTrigger = true;
                        mainCollider.enabled = true;
                        // 降低50%碰撞箱大小（只在第一次调用时执行）并保存原始值
                        mainCollider.radius *= 0.5f;
                        _originalColliderRadius = mainCollider.radius;
                    }

                    // 添加碰撞转发器
                    var collisionForwarder = spriteSilk.GetComponent<SilkBallCollisionForwarder>();
                    if (collisionForwarder == null)
                    {
                        collisionForwarder = spriteSilk.gameObject.AddComponent<SilkBallCollisionForwarder>();
                        collisionForwarder.parent = this;
                    }
                }
            }

            // 获取 Glow 子物体引用（只在第一次调用时）
            if (Glow == null)
            {
                Glow = transform.Find("Glow");
                if (Glow != null)
                {
                }
            }

            // 获取粒子系统并设置大小为两倍（只在第一次调用时）
            if (ptCollect == null)
            {
                var ptCollectTransform = transform.Find("Pt Collect");
                if (ptCollectTransform != null)
                {
                    ptCollect = ptCollectTransform.GetComponent<ParticleSystem>();
                    if (ptCollect != null)
                    {
                        ptCollectTransform.localScale = Vector3.one * 2f;
                    }
                }
            }

            if (ptDisappear == null)
            {
                var ptDisappearTransform = transform.Find("Pt Disappear");
                if (ptDisappearTransform != null)
                {
                    ptDisappear = ptDisappearTransform.GetComponent<ParticleSystem>();
                    if (ptDisappear != null)
                    {
                        ptDisappearTransform.localScale = Vector3.one * 2f;
                    }
                }
            }

            // 在 Sprite Silk 子物体上添加/配置 DamageHero 组件（只在第一次调用时）
            if (damageHero == null && spriteSilk != null)
            {
                damageHero = spriteSilk.GetComponent<DamageHero>();
                if (damageHero == null)
                {
                    damageHero = spriteSilk.gameObject.AddComponent<DamageHero>();
                }
                damageHero.damageDealt = 1;
                damageHero.hazardType = GlobalEnums.HazardType.ENEMY;
                damageHero.overrideCollisionSide = true;
                damageHero.collisionSide = GlobalEnums.CollisionSide.top;
                damageHero.canClashTink = false;
                damageHero.noClashFreeze = true;
                damageHero.noTerrainThunk = true;
                damageHero.noTerrainRecoil = true;
                damageHero.hasNonBouncer = false;
                damageHero.overrideCollisionSide = false;
                // 如果已经获取到原始 DamageHero 引用，复制相关属性
                if (originalDamageHero != null)
                {
                    // 复制 OnDamagedHero 的监听器
                    if (originalDamageHero.OnDamagedHero != null)
                    {
                        damageHero.OnDamagedHero = originalDamageHero.OnDamagedHero;
                    }

                }
                else
                {
                    // 确保 OnDamagedHero 不为 null
                    if (damageHero.OnDamagedHero == null)
                    {
                        damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
                    }
                }
                // ⚠️ 关键修复：创建后默认禁用伤害组件，等待 PrepareForUse 中的延迟激活
                damageHero.enabled = false;
            }

            // 查找玩家（只在第一次调用时）
            if (playerTransform == null)
            {
                var heroController = FindFirstObjectByType<HeroController>();
                if (heroController != null)
                {
                    playerTransform = heroController.transform;
                }
            }
        }

        /// <summary>
        /// 初始化丝球（首次创建时调用，只调用一次）
        /// </summary>
        /// <param name="poolContainer">对象池容器引用</param>
        /// <param name="manager">丝球管理器引用（可选，如果未传入则尝试查找）</param>
        public void InitializeOnce(Transform poolContainer, Managers.SilkBallManager? manager = null)
        {
            _poolContainer = poolContainer;
            gameObject.SetActive(true);
            // 设置 silkBallManager 引用
            if (manager != null)
            {
                silkBallManager = manager;
            }

            // 确保组件引用已获取（Awake可能还没执行）
            if (silkBallManager == null)
            {
                GetComponentReferences();
            }

            // 验证 silkBallManager 是否成功获取
            if (silkBallManager == null)
            {
                Log.Error("无法获取 SilkBallManager 引用，InitializeOnce 失败");
                return;
            }

            // 设置层级为 Enemy Attack
            gameObject.layer = LayerMask.NameToLayer("Enemy Attack");
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.layer = LayerMask.NameToLayer("Enemy Attack");
            }

            // 创建 FSM（只创建一次）
            if (controlFSM == null)
            {
                CreateControlFSM();
            }

            // 添加 EventRegister 组件（用于全局事件广播）
            SetupEventRegisters();

            // 确保初始状态正确
            isActive = false;
            _isAvailable = true;
        }

        /// <summary>
        /// 设置 EventRegister 组件，用于响应全局事件广播
        /// 只添加 SILK BALL RELEASE 事件注册器
        /// ATTACK CLEAR 通过 SilkBallManager.RecycleAllActiveSilkBalls() 处理
        /// </summary>
        private void SetupEventRegisters()
        {
            // 添加 SILK BALL RELEASE 事件注册器
            // 该事件会被转发给 controlFSM，FSM 状态决定是否响应
            // Idle 状态不响应（没有对应转换），Prepare 状态才响应
            // 这样可以确保池内的丝球不会被意外释放
            var existingRegisters = gameObject.GetComponents<EventRegister>();
            EventRegister? releaseRegister = null;
            
            // 查找是否已有 SILK BALL RELEASE 的注册器
            foreach (var reg in existingRegisters)
            {
                if (reg.SubscribedEvent == "SILK BALL RELEASE")
                {
                    releaseRegister = reg;
                    break;
                }
            }
            
            // 如果没有，创建新的
            if (releaseRegister == null)
            {
                releaseRegister = gameObject.AddComponent<EventRegister>();
            }
            
            // 通过反射设置私有字段
            SetEventRegisterFields(releaseRegister, "SILK BALL RELEASE", controlFSM);
        }

        /// <summary>
        /// 通过反射设置 EventRegister 的私有字段
        /// </summary>
        private void SetEventRegisterFields(EventRegister register, string eventName, PlayMakerFSM? targetFsm)
        {
            var type = register.GetType();
            var subscribedEventField = type.GetField("subscribedEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var targetFsmField = type.GetField("targetFsm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (subscribedEventField != null)
            {
                subscribedEventField.SetValue(register, eventName);
            }
            if (targetFsmField != null)
            {
                targetFsmField.SetValue(register, targetFsm);
            }

            // 调用 SwitchEvent 来更新 hash 和注册
            register.SubscribedEvent = eventName;
        }

        /// <summary>
        /// 准备丝球（每次从池中取出时调用）
        /// </summary>
        public void PrepareForUse(Vector3 spawnPosition, float acceleration = 30f, float maxSpeed = 20f, float chaseTime = 6f, float scale = 1f, bool enableRotation = true, Transform? customTarget = null, bool ignoreWall = false, bool delayDamageActivation = true)
        {
            // 脱离池容器
            if (transform.parent == _poolContainer)
            {
                transform.SetParent(null);
            }

            // 设置位置
            transform.position = spawnPosition;

            // 设置参数
            this.acceleration = acceleration;
            this.maxSpeed = maxSpeed;
            this.chaseTime = chaseTime;
            this.scale = scale;
            this.enableRotation = enableRotation;
            this.customTarget = customTarget;
            this.ignoreWallCollision = ignoreWall;
            _delayDamageActivation = delayDamageActivation;

            // 应用缩放
            ApplyScale();

            // 重置状态
            ResetState();
            // 标记为不可用（正在使用中）
            _isAvailable = false;
            isActive = true;

            // ⚠️ 确保FSM已经初始化并处于Idle状态
            if (controlFSM != null)
            {
                // 如果FSM还在Init状态，强制切换到Idle
                if (controlFSM.Fsm.ActiveStateName == "Init")
                {
                    controlFSM.Fsm.SetState("Idle");
                }

                // 发送 PREPARE 事件
                controlFSM.SendEvent("PREPARE");
            }
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        private void ResetState()
        {
            isChasing = false;
            isPrepared = false;
            isProtected = false;
            canBeAbsorbed = false;

            // 重置速度
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.gravityScale = 0f;
            }

            // 先禁用伤害组件，然后延后0.15s激活
            if (damageHero != null)
            {
                if (_delayDamageActivation)
                {
                    damageHero.enabled = false;
                    StartCoroutine(EnableDamageAfterDelay(0.15f));
                }
                else
                {
                    damageHero.enabled = true;
                }
            }

            // 启用碰撞
            if (mainCollider != null)
            {
                mainCollider.enabled = true;
            }

            // 显示 Sprite Silk 和 Glow
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.SetActive(true);
            }
            if (Glow != null)
            {
                Glow.gameObject.SetActive(true);
            }

            // 更新 FSM 变量
            if (readyVar != null)
            {
                readyVar.Value = false;
            }
            if (accelerationVar != null)
            {
                accelerationVar.Value = acceleration;
            }
            if (maxSpeedVar != null)
            {
                maxSpeedVar.Value = maxSpeed;
            }
        }

        /// <summary>
        /// 延迟激活伤害组件
        /// </summary>
        private IEnumerator EnableDamageAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            // 只在丝球仍处于活跃状态时才激活伤害组件
            if (isActive && damageHero != null)
            {
                damageHero.enabled = true;
            }
        }

        /// <summary>
        /// 应用整体缩放
        /// </summary>
        private void ApplyScale()
        {
            // 缩放根物体
            transform.localScale = Vector3.one * scale;

            // 使用原始碰撞器半径乘以scale，避免累积乘法
            if (mainCollider != null && _originalColliderRadius > 0)
            {
                mainCollider.radius = _originalColliderRadius * scale;
            }
        }

        /// <summary>
        /// 创建 Control FSM
        /// </summary>
        private void CreateControlFSM()
        {
            if (controlFSM != null)
            {
                Log.Warn("Control FSM 已存在");
                return;
            }

            // 创建 FSM 组件
            controlFSM = gameObject.AddComponent<PlayMakerFSM>();
            controlFSM.FsmName = "Control";

            // 创建所有状态（在注册事件之前）
            var initState = CreateInitState();
            var idleState = CreateIdleState();
            var prepareState = CreatePrepareState();
            var chaseState = CreateChaseHeroState();
            var disperseState = CreateDisperseState();
            var hitHeroDisperseState = CreateHitHeroDisperseState();
            var disappearState = CreateDisappearState();
            var recycleState = CreateRecycleState();
            var hasGravityState = CreateHasGravityState();

            // 设置状态到 FSM（必须在最早设置）
            controlFSM.Fsm.States = new FsmState[]
            {   initState,
                idleState,
                prepareState,
                chaseState,
                disperseState,
                hitHeroDisperseState,
                disappearState,
                recycleState,
                hasGravityState
            };
            // 注册所有事件
            RegisterFSMEvents();

            // 创建 FSM 变量
            CreateFSMVariables();
            // 添加状态动作
            AddInitActions(initState);
            AddIdleActions(idleState);
            AddPrepareActions(prepareState);
            AddChaseHeroActions(chaseState);
            AddDisperseActions(disperseState);
            AddHitHeroDisperseActions(hitHeroDisperseState);
            AddDisappearActions(disappearState);
            AddRecycleActions(recycleState);
            AddHasGravityActions(hasGravityState);
            // 添加状态转换
            SetFinishedTransition(initState, idleState);
            idleState.Transitions = new FsmTransition[] { CreateTransition(prepareEvent!, prepareState) };
            prepareState.Transitions = new FsmTransition[]
            {
                CreateTransition(releaseEvent!, chaseState),
                CreateTransition(hasGravityEvent!, hasGravityState)
            };
            chaseState.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(disappearState),
                CreateTransition(hitWallEvent!, disperseState),
                CreateTransition(hitHeroEvent!, hitHeroDisperseState)
            };
            SetFinishedTransition(disperseState, recycleState);
            SetFinishedTransition(hitHeroDisperseState, recycleState);
            SetFinishedTransition(disappearState, recycleState);
            hasGravityState.Transitions = new FsmTransition[]
            {
                CreateTransition(hitWallEvent!, disperseState),
                CreateTransition(hitHeroEvent!, hitHeroDisperseState)
            };
            // 初始化 FSM 数据
            controlFSM.Fsm.InitData();

            controlFSM.Fsm.HandleFixedUpdate = true;
            controlFSM.AddEventHandlerComponents();

            // 确保 Started 标记为 true（PlayMakerFSM.Start() 会检查这个）
            // 通过反射设置 Started 为 true
            var fsmType = controlFSM.Fsm.GetType();
            var startedField = fsmType.GetField("started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (startedField != null)
            {
                startedField.SetValue(controlFSM.Fsm, true);
            }
            // 立即设置初始状态（使用字符串）
            controlFSM.Fsm.SetState(initState.Name);
        }

        #region FSM 事件和变量注册
        /// <summary>
        /// 注册 FSM 事件
        /// </summary>
        private void RegisterFSMEvents()
        {
            prepareEvent = FsmEvent.GetFsmEvent("PREPARE");
            releaseEvent = FsmEvent.GetFsmEvent("SILK BALL RELEASE");
            hitWallEvent = FsmEvent.GetFsmEvent("HIT WALL");
            hitHeroEvent = FsmEvent.GetFsmEvent("HIT HERO");
            hasGravityEvent = FsmEvent.GetFsmEvent("HAS_GRAVITY");

            var events = controlFSM!.Fsm.Events.ToList();
            if (!events.Contains(prepareEvent)) events.Add(prepareEvent);
            if (!events.Contains(releaseEvent)) events.Add(releaseEvent);
            if (!events.Contains(hitWallEvent)) events.Add(hitWallEvent);
            if (!events.Contains(hitHeroEvent)) events.Add(hitHeroEvent);
            if (!events.Contains(hasGravityEvent)) events.Add(hasGravityEvent);

            controlFSM.Fsm.Events = events.ToArray();
        }

        /// <summary>
        /// 创建 FSM 变量
        /// </summary>
        private void CreateFSMVariables()
        {
            // Bool 变量
            readyVar = new FsmBool("Ready") { Value = false };
            controlFSM!.FsmVariables.BoolVariables = new FsmBool[] { readyVar };

            // Float 变量
            accelerationVar = new FsmFloat("Acceleration") { Value = acceleration };
            maxSpeedVar = new FsmFloat("MaxSpeed") { Value = maxSpeed };
            controlFSM.FsmVariables.FloatVariables = new FsmFloat[] { accelerationVar, maxSpeedVar };

            targetGameObjectVar = new FsmGameObject("Target") { Value = null };
            controlFSM.FsmVariables.GameObjectVariables = new FsmGameObject[] { targetGameObjectVar };

            controlFSM.FsmVariables.Init();
        }
        #endregion

        #region 创建状态
        private FsmState CreateInitState()
        {
            return CreateState(controlFSM!.Fsm, "Init", "初始化状态");
        }
        private FsmState CreateIdleState()
        {
            return CreateState(controlFSM!.Fsm, "Idle", "静默状态，等待 PREPARE 事件");
        }

        private FsmState CreatePrepareState()
        {
            return CreateState(controlFSM!.Fsm, "Prepare", "准备状态，等待释放");
        }

        private FsmState CreateChaseHeroState()
        {
            return CreateState(controlFSM!.Fsm, "Chase Hero", "追逐玩家");
        }

        private FsmState CreateDisperseState()
        {
            return CreateState(controlFSM!.Fsm, "Disperse", "快速消散（碰到墙壁）");
        }

        private FsmState CreateHitHeroDisperseState()
        {
            return CreateState(controlFSM!.Fsm, "Hit Hero Disperse", "碰到玩家消散（保持伤害启用）");
        }

        private FsmState CreateDisappearState()
        {
            return CreateState(controlFSM!.Fsm, "Disappear", "缓慢消失");
        }

        private FsmState CreateRecycleState()
        {
            return CreateState(controlFSM!.Fsm, "Recycle", "回收到对象池");
        }

        private FsmState CreateHasGravityState()
        {
            return CreateState(controlFSM!.Fsm, "Has Gravity", "有重力状态");
        }
        #endregion

        #region 添加状态动作
        private void AddInitActions(FsmState initState)
        {
            // Init状态：首次创建后立即进入Idle，无需等待
            // 这个状态只在对象首次创建时执行一次
            // var waitAction = new Wait
            // {
            //     time = new FsmFloat(0.01f),  // 极短等待，确保FSM完全初始化
            //     finishEvent = FsmEvent.Finished
            // };

            initState.Actions = new FsmStateAction[] { };
        }
        private void AddIdleActions(FsmState idleState)
        {
            // Idle 状态：对象池待命状态，无限等待PREPARE事件
            // 不需要任何Action，纯粹等待事件触发
            idleState.Actions = new FsmStateAction[] { };
        }

        private void AddPrepareActions(FsmState prepareState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 设置 Ready 为 false（准备中）
            actions.Add(new SetBoolValue
            {
                boolVariable = readyVar,
                boolValue = new FsmBool(false)
            });

            // 2. 速度归零（确保丝球静止）
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = Vector2.zero, UseVariable = false },
                x = new FsmFloat { Value = 0f, UseVariable = false },
                y = new FsmFloat { Value = 0f, UseVariable = false },
                everyFrame = false
            });

            // 3. 播放 Init 音效（如果有）
            if (silkBallManager?.InitAudioTable != null && silkBallManager?.InitAudioPlayerPrefab != null)
            {
                actions.Add(new PlayRandomAudioClipTable
                {
                    Table = silkBallManager.InitAudioTable,
                    AudioPlayerPrefab = silkBallManager.InitAudioPlayerPrefab,
                    SpawnPoint = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner,
                        GameObject = new FsmGameObject { Value = gameObject }
                    },
                    SpawnPosition = new FsmVector3 { Value = Vector3.zero }
                });
            }
            // 5. 设置 Ready 为 true（准备完成）
            actions.Add(new SetBoolValue
            {
                boolVariable = readyVar,
                boolValue = new FsmBool(true)
            });

            // 6. 标记丝球为已准备状态
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("MarkAsPrepared") { Value = "MarkAsPrepared" },
                parameters = new FsmVar[0]
            });

            prepareState.Actions = actions.ToArray();
        }

        private void AddChaseHeroActions(FsmState chaseState)
        {
            var startChaseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartChase") { Value = "StartChase" },
                parameters = new FsmVar[0]
            };

            var chaseAction = new ChaseTargetAction
            {
                targetGameObject = targetGameObjectVar!,
                acceleration = accelerationVar ?? new FsmFloat { Value = acceleration },
                maxSpeed = maxSpeedVar ?? new FsmFloat { Value = maxSpeed },
                useRigidbody = new FsmBool { Value = true },
                chaseTime = new FsmFloat { Value = 0f },
                reachDistance = new FsmFloat { Value = 0.1f },
                onReachTarget = null,
                onTimeout = null
            };

            // 超时等待
            var waitAction = new Wait
            {
                time = new FsmFloat(chaseTime),
                finishEvent = FsmEvent.Finished
            };

            chaseState.Actions = new FsmStateAction[] { startChaseAction, chaseAction, waitAction };
        }

        private void AddDisperseActions(FsmState disperseState)
        {
            // 禁用伤害和隐藏 Sprite Silk
            var disableVisualAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DisableDamageAndVisual") { Value = "DisableDamageAndVisual" },
                parameters = new FsmVar[0]
            };

            // 停止追踪
            var stopChaseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            };

            // 速度衰减
            var slowDownAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SlowDownToZero") { Value = "SlowDownToZero" },
                parameters = new FsmVar[0]
            };

            // 播放消散粒子
            var playParticleAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PlayCollectParticle") { Value = "PlayCollectParticle" },
                parameters = new FsmVar[0]
            };

            // 等待粒子播放完毕（Pt Collect 粒子持续时间约 0.5 秒）
            var finishAction = new Wait
            {
                time = new FsmFloat(0.6f),  // 等待粒子播放完毕
                finishEvent = FsmEvent.Finished
            };

            // 创建 Get Silk 音效动作
            FsmStateAction? getSilkAudio = null;
            if (silkBallManager?.GetSilkAudioTable != null && silkBallManager?.GetSilkAudioPlayerPrefab != null)
            {
                getSilkAudio = new PlayRandomAudioClipTable
                {
                    Table = silkBallManager.GetSilkAudioTable,
                    AudioPlayerPrefab = silkBallManager.GetSilkAudioPlayerPrefab,
                    SpawnPoint = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner,
                        GameObject = new FsmGameObject { Value = gameObject }
                    },
                    SpawnPosition = new FsmVector3 { Value = Vector3.zero },
                    fsmState = disperseState,
                    fsmComponent = controlFSM,
                    fsm = controlFSM?.Fsm,
                    owner = controlFSM?.gameObject
                };
            }

            // 添加动作：禁用视觉和伤害 -> 停止追踪 -> 减速 -> 播放粒子 -> 播放音效 -> 等待粒子播放完毕
            if (getSilkAudio != null)
            {
                disperseState.Actions = new FsmStateAction[]
                {
                    disableVisualAction,
                    stopChaseAction,
                    slowDownAction,
                    playParticleAction,
                    getSilkAudio,
                    finishAction
                };
            }
            else
            {
                disperseState.Actions = new FsmStateAction[]
                {
                    disableVisualAction,
                    stopChaseAction,
                    slowDownAction,
                    playParticleAction,
                    finishAction
                };
            }
        }

        private void AddHitHeroDisperseActions(FsmState hitHeroDisperseState)
        {
            // 延迟禁用伤害和视觉（使用协程延迟 0.1 秒）
            var delayDisableAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DelayDisableDamageAndVisual") { Value = "DelayDisableDamageAndVisual" },
                parameters = new FsmVar[0]
            };

            // 停止追踪
            var stopChaseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            };

            // 速度衰减
            var slowDownAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SlowDownToZero") { Value = "SlowDownToZero" },
                parameters = new FsmVar[0]
            };

            // 播放消散粒子
            var playParticleAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PlayCollectParticle") { Value = "PlayCollectParticle" },
                parameters = new FsmVar[0]
            };

            // 等待粒子播放完毕
            var waitFinishAction = new Wait
            {
                time = new FsmFloat(0.6f),  // 等待粒子播放完毕
                finishEvent = FsmEvent.Finished
            };

            // 创建 Get Silk 音效动作
            FsmStateAction? getSilkAudio = null;
            if (silkBallManager?.GetSilkAudioTable != null && silkBallManager?.GetSilkAudioPlayerPrefab != null)
            {
                getSilkAudio = new PlayRandomAudioClipTable
                {
                    Table = silkBallManager.GetSilkAudioTable,
                    AudioPlayerPrefab = silkBallManager.GetSilkAudioPlayerPrefab,
                    SpawnPoint = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner,
                        GameObject = new FsmGameObject { Value = gameObject }
                    },
                    SpawnPosition = new FsmVector3 { Value = Vector3.zero },
                    fsmState = hitHeroDisperseState,
                    fsmComponent = controlFSM,
                    fsm = controlFSM?.Fsm,
                    owner = controlFSM?.gameObject
                };
            }

            // 添加动作：延迟禁用伤害 -> 停止追踪 -> 减速 -> 播放粒子 -> 播放音效 -> 等待粒子播放完毕
            if (getSilkAudio != null)
            {
                hitHeroDisperseState.Actions = new FsmStateAction[]
                {
                    delayDisableAction,
                    stopChaseAction,
                    slowDownAction,
                    playParticleAction,
                    getSilkAudio,
                    waitFinishAction
                };
            }
            else
            {
                hitHeroDisperseState.Actions = new FsmStateAction[]
                {
                    delayDisableAction,
                    stopChaseAction,
                    slowDownAction,
                    playParticleAction,
                    waitFinishAction
                };
            }
        }

        private void AddDisappearActions(FsmState disappearState)
        {
            // 禁用伤害
            var disableDamageAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DisableDamage") { Value = "DisableDamage" },
                parameters = new FsmVar[0]
            };

            // 停止追踪
            var stopChaseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            };

            // 速度衰减
            var slowDownAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SlowDownToZero") { Value = "SlowDownToZero" },
                parameters = new FsmVar[0]
            };

            // 播放消散粒子（使用 Pt Collect，和碰墙消散一样）
            var playParticleAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PlayCollectParticle") { Value = "PlayCollectParticle" },
                parameters = new FsmVar[0]
            };

            // 播放消失动画（Sprite Silk 的 "Bundle Disappear" 动画）
            var playAnimAction = new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    GameObject = spriteSilk != null ? new FsmGameObject { Value = spriteSilk.gameObject } : new FsmGameObject()
                },
                clipName = new FsmString("Bundle Disappear") { Value = "Bundle Disappear" },
                animationTriggerEvent = null,  // 不在动画触发时发送事件
                animationCompleteEvent = null  // 不在动画完成时发送事件
            };

            // 等待动画和粒子播放完毕（增加 0.3 秒让动画完整播放）
            var waitAction = new Wait
            {
                time = new FsmFloat(0.9f),  // 0.6 秒粒子 + 0.3 秒额外动画时间
                finishEvent = FsmEvent.Finished
            };

            disappearState.Actions = new FsmStateAction[]
            {
                disableDamageAction,
                stopChaseAction,
                slowDownAction,
                playParticleAction,
                playAnimAction,
                waitAction
            };
        }

        private void AddRecycleActions(FsmState recycleState)
        {
            var recycleAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("RecycleToPool") { Value = "RecycleToPool" },
                parameters = new FsmVar[0]
            };

            recycleState.Actions = new FsmStateAction[] { recycleAction };
        }

        private void AddHasGravityActions(FsmState hasGravityState)
        {
            // 设置重力
            var setGravityAction = new SetGravity2dScale
            {
                gravityScale = new FsmFloat(1f)
            };

            // 停止追踪
            var stopChaseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            };

            // 等待碰撞
            var waitAction = new Wait
            {
                time = new FsmFloat(10f),
                finishEvent = FsmEvent.Finished
            };

            hasGravityState.Actions = new FsmStateAction[] { setGravityAction, stopChaseAction, waitAction };
        }
        #endregion



        #region 辅助方法（供FSM调用）
        public void StartChase()
        {
            isChasing = true;

            GameObject? target = null;
            if (customTarget != null)
            {
                target = customTarget.gameObject;
            }
            else
            {
                var heroController = FindFirstObjectByType<HeroController>();
                if (heroController != null)
                {
                    target = heroController.gameObject;
                }
            }

            if (targetGameObjectVar != null && target != null)
            {
                targetGameObjectVar.Value = target;
            }
        }

        /// <summary>
        /// 停止追踪
        /// </summary>
        public void StopChase()
        {
            isChasing = false;
        }

        /// <summary>
        /// 速度衰减到0
        /// </summary>
        public void SlowDownToZero()
        {
            StartCoroutine(SlowDownCoroutine());
        }

        private IEnumerator SlowDownCoroutine()
        {
            if (rb2d == null) yield break;

            float duration = 0.2f;
            float elapsed = 0f;
            Vector2 startVelocity = rb2d.linearVelocity;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                rb2d.linearVelocity = Vector2.Lerp(startVelocity, Vector2.zero, t);
                yield return null;
            }

            rb2d.linearVelocity = Vector2.zero;
        }

        /// <summary>
        /// 禁用伤害
        /// </summary>
        public void DisableDamage()
        {
            if (damageHero != null)
            {
                damageHero.enabled = false;
            }
            if (mainCollider != null)
            {
                mainCollider.enabled = false;
            }
        }
        public void SendEvent(string eventName)
        {
            if (controlFSM != null)
            {
                controlFSM.SendEvent(eventName);
            }
        }
        /// <summary>
        /// 禁用伤害并隐藏 Sprite Silk 和 Glow
        /// </summary>
        public void DisableDamageAndVisual()
        {
            // 禁用伤害
            DisableDamage();

            // 隐藏 Sprite Silk 和 Glow 子物体
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.SetActive(false);
            }
            if (Glow != null)
            {
                Glow.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 延迟禁用伤害并隐藏 Sprite Silk（用于碰到玩家的情况，让伤害先生效）
        /// </summary>
        public void DelayDisableDamageAndVisual()
        {
            StartCoroutine(DelayDisableDamageAndVisualCoroutine());
        }

        private IEnumerator DelayDisableDamageAndVisualCoroutine()
        {
            // 等待 0.03 秒让伤害生效
            yield return new WaitForSeconds(0.03f);

            // 禁用伤害并隐藏视觉
            DisableDamageAndVisual();
        }

        /// <summary>
        /// 播放快速消散粒子
        /// </summary>
        public void PlayCollectParticle()
        {
            if (ptCollect != null)
            {
                ptCollect.Play();
                // Debug.Log("播放消散粒子");
            }
        }

        /// <summary>
        /// 回收到对象池
        /// </summary>
        public void RecycleToPool()
        {
            // 停止所有正在运行的协程
            StopAllCoroutines();

            // 标记为不活跃
            isActive = false;
            _isAvailable = true;

            // 重置状态标记
            isChasing = false;
            isPrepared = false;
            isProtected = false;
            canBeAbsorbed = false;

            // 重置速度
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.gravityScale = 0f;
            }

            // ⚠️ 关键修复：回收时禁用伤害组件，确保下次从池中取出时不会立即造成伤害
            if (damageHero != null)
            {
                damageHero.enabled = false;
            }

            // 清空自定义目标
            customTarget = null;
            // 隐藏所有子物体（包括Sprite Silk）
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.SetActive(false);
            }
            if (Glow != null)
            {
                Glow.gameObject.SetActive(false);
            }
            // 禁用所有碰撞转发器
            var forwarders = GetComponentsInChildren<SilkBallCollisionForwarder>();
            foreach (var forwarder in forwarders)
            {
                forwarder.enabled = false;
            }

            // 重置FSM变量
            if (readyVar != null)
            {
                readyVar.Value = false;
            }

            // 回到池容器
            if (_poolContainer != null)
            {
                transform.SetParent(_poolContainer);
            }

            // 重置到 Idle 状态（等待下次使用）
            if (controlFSM != null)
            {
                controlFSM.Fsm.SetState("Idle");
            }
        }

        /// <summary>
        /// 回收到对象池（带Z轴过渡动画，用于被大丝球吸收）
        /// </summary>
        public void RecycleToPoolWithZTransition()
        {
            StartCoroutine(RecycleToPoolWithZTransitionCoroutine());
        }

        /// <summary>
        /// 回收协程：回收到对象池
        /// </summary>
        private IEnumerator RecycleToPoolWithZTransitionCoroutine()
        {
            // 停止追踪
            isChasing = false;
            canBeAbsorbed = false;

            // 禁用伤害和碰撞
            DisableDamage();

            if (mainCollider != null)
            {
                mainCollider.enabled = false;
            }

            yield return null;

            // 回收到对象池
            RecycleToPool();
        }

        /// <summary>
        /// 设置初始速度（供Has Gravity状态使用）
        /// </summary>
        public void SetInitialVelocity(Vector2 velocity)
        {
            if (rb2d != null)
            {
                rb2d.linearVelocity = velocity;
            }
        }

        /// <summary>
        /// 设置自转开关
        /// </summary>
        public void SetRotation(bool enable)
        {
            enableRotation = enable;
            //Log.Info($"丝球自转已{(enable ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 标记丝球为已准备状态
        /// </summary>
        public void MarkAsPrepared()
        {
            isPrepared = true;
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.SetActive(true);
            }
            if (Glow != null)
            {
                Glow.gameObject.SetActive(true);
            }
            var forwarders = GetComponentsInChildren<SilkBallCollisionForwarder>();
            foreach (var forwarder in forwarders)
            {
                forwarder.enabled = true;
            }
        }
        #endregion

        #region 碰撞检测
        /// <summary>
        /// 处理碰撞（由碰撞转发器调用）
        /// </summary>
        public void HandleCollision(GameObject otherObject)
        {
            if (!isActive || controlFSM == null)
            {
                Debug.Log($"碰撞忽略: isActive={isActive}, controlFSM={controlFSM != null}");
                return;
            }

            // 检查大丝球碰撞箱（追踪大丝球的小丝球会被吸收）
            var collisionBox = otherObject.GetComponent<BigSilkBallCollisionBox>();
            if (collisionBox != null)
            {
                return;
            }

            // 检查墙壁碰撞（立即消散）
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            int defaultLayer = LayerMask.NameToLayer("Default");

            if (otherObject.layer == terrainLayer || otherObject.layer == defaultLayer)
            {
                // 如果设置了忽略墙壁碰撞，则不处理
                if (ignoreWallCollision)
                {
                    return;
                }

                // 如果处于保护时间内，忽略墙壁碰撞
                if (isProtected)
                {
                    return;
                }

                controlFSM.SendEvent("HIT WALL");
                return;
            }

            // 检查玩家碰撞（Hero Box 图层）
            int heroBoxLayer = LayerMask.NameToLayer("Hero Box");

            if (otherObject.layer == heroBoxLayer)
            {

                // Debug.Log($"碰到玩家: {otherObject.name}, 发送 HIT HERO 事件");
                controlFSM.SendEvent("HIT HERO");
                return;
            }
        }

        /// <summary>
        /// 启动保护时间（在此期间不会因碰到英雄或墙壁消失）
        /// </summary>
        public void StartProtectionTime(float duration)
        {
            StartCoroutine(ProtectionTimeCoroutine(duration));
        }

        /// <summary>
        /// 保护时间协程
        /// </summary>
        private IEnumerator ProtectionTimeCoroutine(float duration)
        {
            isProtected = true;
            yield return new WaitForSeconds(duration);
            isProtected = false;
        }
        #endregion
    }

    #region 碰撞转发器
    /// <summary>
    /// 碰撞转发器 - 将子物体的碰撞事件转发给父 SilkBallBehavior
    /// </summary>
    internal class SilkBallCollisionForwarder : MonoBehaviour
    {
        public SilkBallBehavior? parent;

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 转发给HandleCollision处理所有碰撞
            if (parent != null)
            {
                parent.HandleCollision(other.gameObject);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // 转发给HandleCollision处理所有碰撞
            if (parent != null)
            {
                parent.HandleCollision(collision.gameObject);
            }
        }
    }
    #endregion
}
