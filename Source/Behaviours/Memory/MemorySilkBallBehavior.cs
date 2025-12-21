using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Actions;
using System.Collections.Generic;
using System;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 丝球Behavior - 管理单个丝球的行为和FSM
    /// </summary>
    internal class MemorySilkBallBehavior : MonoBehaviour
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

        [Header("反向加速度参数")]
        public Vector3 reverseAccelCenter = Vector3.zero;   // 反向加速度指向的圆心
        public float reverseAccelValue = 20f;               // 反向加速度值
        public float initialOutwardSpeed = 15f;             // 初始向外速度
        public float maxInwardSpeed = 25f;                  // 最大向内速度
        public float reverseAccelDuration = 10f;            // 反向加速度状态最大持续时间
        private bool isInReverseAccelMode = false;          // 是否处于反向加速度模式
        private bool hasReversedDirection = false;          // 速度是否已反转方向

        [Header("公转参数（最终爆炸阶段）")]
        private bool isInOrbitalMode = false;               // 是否处于公转模式
        private Vector3 orbitalCenter;                      // 公转圆心
        private float orbitalAngularSpeed = 0f;             // 公转角速度（度/秒，正=逆时针，负=顺时针）
        private float orbitalOutwardSpeed = 0f;             // 径向向外速度
        #endregion

        #region 状态标记
        [Header("状态标记")]
        public bool isActive = false;             // 是否激活
        public bool isChasing = false;            // 是否正在追踪玩家
        public bool isPrepared = false;           // 是否已准备
        public bool ignoreWallCollision = false;  // 是否忽略墙壁碰撞（用于追踪大丝球的小丝球）
        private bool isProtected = false;         // 是否处于保护时间内（不会因碰到英雄或墙壁消失）
        public bool canBeAbsorbed = false;        // 是否可以被大丝球吸收（仅吸收阶段的小丝球为true）
        public bool triggerBlastOnDestroy = false; // 是否在销毁时触发 Blast 攻击

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
        private SilkBallManager? silkBallManager;  // 丝球管理器
        private DamageHero? originalDamageHero;            // 原始 DamageHero 组件引用
        private FWBlastManager? _blastManager;             // Blast 管理器引用

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
        private FsmEvent? reverseAccelEvent;    // 反向加速度状态事件
        private FsmEvent? hasGravityEvent;      // 重力状态事件
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

            // 处理公转运动（最终爆炸阶段的螺旋向外运动）
            if (isActive && isInOrbitalMode && rb2d != null)
            {
                UpdateOrbitalMotion();
            }
        }

        /// <summary>
        /// 获取组件引用
        /// </summary>
        private void GetComponentReferences()
        {
            // 首先获取 SilkBallManager
            if (silkBallManager == null || _blastManager == null)
            {
                var managerObj = GameObject.Find("AnySilkBossManager");
                if (managerObj != null)
                {
                    if (silkBallManager == null)
                    {
                        silkBallManager = managerObj.GetComponent<SilkBallManager>();
                        if (silkBallManager == null)
                        {
                            Log.Warn("AnySilkBossManager 上未找到 SilkBallManager 组件");
                        }
                    }
                    if (_blastManager == null)
                    {
                        _blastManager = managerObj.GetComponent<FWBlastManager>();
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
                    var collisionForwarder = spriteSilk.GetComponent<MemorySilkBallCollisionForwarder>();
                    if (collisionForwarder == null)
                    {
                        collisionForwarder = spriteSilk.gameObject.AddComponent<MemorySilkBallCollisionForwarder>();
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
        public void PrepareForUse(Vector3 spawnPosition, float acceleration = 30f, float maxSpeed = 20f, float chaseTime = 6f, float scale = 1f, bool enableRotation = true, Transform? customTarget = null, bool ignoreWall = false)
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
                controlFSM.SendEvent("PREPARE");
            }
        }

        #region 外部调用基元接口
        /// <summary>
        /// 设置物理参数（重力、速度）
        /// </summary>
        public void SetPhysics(Vector2 velocity, float gravityScale = 0f)
        {
            if (rb2d != null)
            {
                rb2d.gravityScale = gravityScale;
                rb2d.bodyType = RigidbodyType2D.Dynamic;
                rb2d.linearVelocity = velocity;
            }
        }

        /// <summary>
        /// 发送FSM事件
        /// </summary>
        public void SendFsmEvent(string eventName)
        {
            if (controlFSM != null)
            {
                controlFSM.SendEvent(eventName);
            }
        }
        #endregion

        /// <summary>
        /// 重置状态
        /// </summary>
        private void ResetState()
        {
            isChasing = false;
            isPrepared = false;
            isProtected = false;
            canBeAbsorbed = false;
            triggerBlastOnDestroy = false;

            // 重置速度
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.gravityScale = 0f;
            }

            // 先禁用伤害组件，然后延后0.15s激活
            if (damageHero != null)
            {
                damageHero.enabled = false;
                StartCoroutine(EnableDamageAfterDelay(0.15f));
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
            // 缩放根物体（Unity会自动缩放子物体的碰撞箱世界大小）
            transform.localScale = Vector3.one * scale;

            // 碰撞箱本地半径保持不变，不需要手动乘以scale
            // 因为Unity会根据父物体的scale自动调整碰撞箱的世界大小
            if (mainCollider != null && _originalColliderRadius > 0)
            {
                mainCollider.radius = _originalColliderRadius;
            }
        }

        /// <summary>
        /// 创建 Control FSM（使用 FsmStateBuilder 简化）
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

            // 使用 FsmStateBuilder 批量创建状态
            var states = FsmStateBuilder.CreateStates(controlFSM.Fsm,
                ("Init", "初始化状态"),
                ("Idle", "静默状态，等待 PREPARE 事件"),
                ("Prepare", "准备状态，等待释放"),
                ("Chase Hero", "追逐玩家"),
                ("Disperse", "快速消散（碰到墙壁）"),
                ("Hit Hero Disperse", "碰到玩家消散（保持伤害启用）"),
                ("Disappear", "缓慢消失"),
                ("Recycle", "回收到对象池"),
                ("Has Gravity", "有重力状态"),
                ("Reverse Acceleration", "反向加速度状态 - 初始向外，受到指向圆心的恒定加速度")
            );

            // 解构状态引用
            var initState = states[0];
            var idleState = states[1];
            var prepareState = states[2];
            var chaseState = states[3];
            var disperseState = states[4];
            var hitHeroDisperseState = states[5];
            var disappearState = states[6];
            var recycleState = states[7];
            var hasGravityState = states[8];
            var reverseAccelState = states[9];

            // 设置状态到 FSM
            controlFSM.Fsm.States = states;

            // 使用 FsmStateBuilder 批量注册事件
            var fsmEvents = FsmStateBuilder.RegisterEvents(controlFSM,
                "PREPARE", "SILK BALL RELEASE", "HIT WALL", "HIT HERO", "REVERSE_ACCEL", "HAS_GRAVITY"
            );
            prepareEvent = fsmEvents[0];
            releaseEvent = fsmEvents[1];
            hitWallEvent = fsmEvents[2];
            hitHeroEvent = fsmEvents[3];
            reverseAccelEvent = fsmEvents[4];
            hasGravityEvent = fsmEvents[5];

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
            AddReverseAccelerationActions(reverseAccelState);

            // 使用 FsmStateBuilder 添加状态转换
            FsmStateBuilder.SetFinishedTransition(initState, idleState);
            // Idle 状态可以通过 PREPARE 进入 Prepare，或通过 REVERSE_ACCEL 直接进入 Reverse Acceleration
            idleState.Transitions = new FsmTransition[]
            {
                FsmStateBuilder.CreateTransition(prepareEvent!, prepareState),
                FsmStateBuilder.CreateTransition(reverseAccelEvent!, reverseAccelState)
            };

            // Prepare 状态可以通过 SILK BALL RELEASE 进入 Chase Hero，或通过 HAS_GRAVITY 进入 Has Gravity
            prepareState.Transitions = new FsmTransition[]
            {
                FsmStateBuilder.CreateTransition(releaseEvent!, chaseState),
                FsmStateBuilder.CreateTransition(hasGravityEvent!, hasGravityState),
                FsmStateBuilder.CreateTransition(reverseAccelEvent!, reverseAccelState)
            };
            // Prepare 状态转换已在上面设置

            // Chase Hero 状态转换
            chaseState.Transitions = new FsmTransition[]
            {
                FsmStateBuilder.CreateFinishedTransition(disappearState),
                FsmStateBuilder.CreateTransition(hitWallEvent!, disperseState),
                FsmStateBuilder.CreateTransition(hitHeroEvent!, hitHeroDisperseState)
            };

            FsmStateBuilder.SetFinishedTransition(disperseState, recycleState);
            FsmStateBuilder.SetFinishedTransition(hitHeroDisperseState, recycleState);
            FsmStateBuilder.SetFinishedTransition(disappearState, recycleState);

            // Has Gravity 状态转换
            hasGravityState.Transitions = new FsmTransition[]
            {
                FsmStateBuilder.CreateTransition(hitWallEvent!, disperseState),
                FsmStateBuilder.CreateTransition(hitHeroEvent!, hitHeroDisperseState)
            };

            // Reverse Acceleration 状态转换
            reverseAccelState.Transitions = new FsmTransition[]
            {
                FsmStateBuilder.CreateFinishedTransition(disperseState),
                FsmStateBuilder.CreateTransition(hitWallEvent!, disperseState),
                FsmStateBuilder.CreateTransition(hitHeroEvent!, hitHeroDisperseState)
            };

            // 初始化 FSM 数据
            controlFSM.Fsm.InitData();

            // 确保 Started 标记为 true
            var fsmType = controlFSM.Fsm.GetType();
            var startedField = fsmType.GetField("started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (startedField != null)
            {
                startedField.SetValue(controlFSM.Fsm, true);
            }

            // 立即设置初始状态
            controlFSM.Fsm.SetState(initState.Name);
        }

        #region FSM 变量注册
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

            // GameObject 变量 - 用于存储追踪目标
            targetGameObjectVar = new FsmGameObject("Target") { Value = null };
            controlFSM.FsmVariables.GameObjectVariables = new FsmGameObject[] { targetGameObjectVar };

            controlFSM.FsmVariables.Init();
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
            // 标记开始追踪
            var startChaseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartChase") { Value = "StartChase" },
                parameters = new FsmVar[0]
            };

            // 使用通用的 ChaseTargetAction（自动追踪玩家或 customTarget）
            var chaseAction = new ChaseTargetAction
            {
                targetGameObject = targetGameObjectVar,  // 可以通过 FSM 变量设置目标
                acceleration = accelerationVar ?? new FsmFloat { Value = acceleration },
                maxSpeed = maxSpeedVar ?? new FsmFloat { Value = maxSpeed },
                useRigidbody = new FsmBool { Value = true },
                chaseTime = new FsmFloat { Value = 0f },  // 不使用 Action 的超时，使用 Wait
                reachDistance = new FsmFloat { Value = 0.1f }  // 很小的距离，几乎不会触发到达
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
            // 如果设置了 triggerBlastOnDestroy，触发 Blast
            var triggerBlastAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("TriggerBlastIfNeeded") { Value = "TriggerBlastIfNeeded" },
                parameters = new FsmVar[0]
            };

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

            // 添加动作：触发Blast(如需) -> 禁用视觉和伤害 -> 停止追踪 -> 减速 -> 播放粒子 -> 播放音效 -> 等待粒子播放完毕
            if (getSilkAudio != null)
            {
                disperseState.Actions = new FsmStateAction[]
                {
                    triggerBlastAction,
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
                    triggerBlastAction,
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
            // 如果设置了 triggerBlastOnDestroy，触发 Blast
            var triggerBlastAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("TriggerBlastIfNeeded") { Value = "TriggerBlastIfNeeded" },
                parameters = new FsmVar[0]
            };

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

            // 添加动作：触发Blast(如需) -> 延迟禁用伤害 -> 停止追踪 -> 减速 -> 播放粒子 -> 播放音效 -> 等待粒子播放完毕
            if (getSilkAudio != null)
            {
                hitHeroDisperseState.Actions = new FsmStateAction[]
                {
                    triggerBlastAction,
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
                    triggerBlastAction,
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
            // 不强制设置重力，使用外部已设置的 rb2d.gravityScale
            // 这样可以支持不同的重力值（如抛射阶段使用 0.0005f）

            // 停止追踪
            var stopChaseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            };

            // 等待碰撞（长时间等待，让丝球自然飞行）
            var waitAction = new Wait
            {
                time = new FsmFloat(10f),
                finishEvent = FsmEvent.Finished
            };

            hasGravityState.Actions = new FsmStateAction[] { stopChaseAction, waitAction };
        }

        private void AddReverseAccelerationActions(FsmState reverseAccelState)
        {
            // 使用通用的 ReverseAccelerationAction
            var reverseAccelAction = new ReverseAccelerationAction
            {
                centerPoint = new FsmVector3 { Value = reverseAccelCenter },
                initialOutwardSpeed = new FsmFloat { Value = initialOutwardSpeed },
                reverseAcceleration = new FsmFloat { Value = reverseAccelValue },
                maxInwardSpeed = new FsmFloat { Value = maxInwardSpeed },
                returnThreshold = new FsmFloat { Value = 0.5f },
                maxDuration = new FsmFloat { Value = reverseAccelDuration },
                finishOnWallHit = new FsmBool { Value = true }
            };

            // 超时等待（备用，Action 本身也有超时）
            var waitAction = new Wait
            {
                time = new FsmFloat(reverseAccelDuration),
                finishEvent = FsmEvent.Finished
            };

            reverseAccelState.Actions = new FsmStateAction[] { reverseAccelAction, waitAction };
        }
        #endregion

        #region 辅助方法（供FSM调用）
        /// <summary>
        /// 如果设置了 triggerBlastOnDestroy，在当前位置触发 Blast 攻击
        /// </summary>
        public void TriggerBlastIfNeeded()
        {
            if (!triggerBlastOnDestroy) return;

            if (_blastManager == null)
            {
                // 尝试重新获取
                var managerObj = GameObject.Find("AnySilkBossManager");
                if (managerObj != null)
                {
                    _blastManager = managerObj.GetComponent<FWBlastManager>();
                }
            }
            Log.Debug($"[MemorySilkBall] 这里是移动丝球汇报生成Bomb Blast");
            if (_blastManager != null && _blastManager.IsInitialized)
            {
                _blastManager.SpawnBombBlast(transform.position);
                Log.Debug($"[MemorySilkBall] 触发 Blast at {transform.position}");
            }
            else
            {
                Log.Warn("[MemorySilkBall] FWBlastManager 未就绪，无法触发 Blast");
            }
        }

        /// <summary>
        /// 开始追踪（设置追踪目标到 FSM 变量）
        /// </summary>
        public void StartChase()
        {
            isChasing = true;

            // 设置追踪目标：优先使用 customTarget，否则使用玩家
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

            // 更新 FSM 变量
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
            triggerBlastOnDestroy = false;
            isInReverseAccelMode = false;
            hasReversedDirection = false;
            isInOrbitalMode = false;
            orbitalAngularSpeed = 0f;
            orbitalOutwardSpeed = 0f;

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
            var forwarders = GetComponentsInChildren<MemorySilkBallCollisionForwarder>();
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
        /// 设置重力缩放（供Has Gravity状态使用）
        /// </summary>
        public void SetGravityScale(float gravityScale)
        {
            if (rb2d != null)
            {
                rb2d.gravityScale = gravityScale;
            }
        }

        /// <summary>
        /// 延迟后施加持续加速度（指向玩家）
        /// </summary>
        /// <param name="delay">延迟时间（秒）</param>
        /// <param name="acceleration">加速度大小（单位/秒²）</param>
        /// <param name="duration">加速度持续时间（秒），默认0.5秒</param>
        public void ApplyDelayedBoostTowardsHero(float delay, float acceleration, float duration = 2f)
        {
            StartCoroutine(DelayedBoostCoroutine(delay, acceleration, duration));
        }

        /// <summary>
        /// 延迟加速度协程 - 在延迟后持续施加固定方向的加速度（方向在开始时确定）
        /// </summary>
        private IEnumerator DelayedBoostCoroutine(float delay, float acceleration, float duration)
        {
            yield return new WaitForSeconds(delay);

            if (!isActive || rb2d == null) yield break;

            // 只在开始时获取一次玩家位置，确定加速度方向
            Vector2 direction = Vector2.zero;
            var heroController = FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                direction = ((Vector2)heroController.transform.position - (Vector2)transform.position).normalized;
            }

            if (direction == Vector2.zero) yield break;

            // 逐渐减速到接近0，然后设置初始速度指向玩家
            while (rb2d.linearVelocity.magnitude > 1f)
            {
                rb2d.linearVelocity *= 0.9f;
                yield return new WaitForSeconds(0.018f);
            }
            // 设置初始速度指向玩家
            rb2d.linearVelocity = direction * 8f;
            // 使用固定方向持续施加加速度，限制最大速度为30
            const float maxSpeed = 30f;
            float elapsed = 0f;
            while (elapsed < duration && isActive && rb2d != null)
            {
                // 施加加速度：velocity += acceleration * deltaTime
                rb2d.linearVelocity += direction * acceleration * Time.deltaTime;

                // 限制最大速度
                if (rb2d.linearVelocity.magnitude > maxSpeed)
                {
                    rb2d.linearVelocity = rb2d.linearVelocity.normalized * maxSpeed;
                }

                elapsed += Time.deltaTime;
                yield return null;
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
        /// 启动公转运动（最终爆炸阶段使用）
        /// 丝球将绕圆心做螺旋向外运动：径向向外 + 切向旋转
        /// </summary>
        /// <param name="center">公转圆心（大丝球碰撞箱位置快照）</param>
        /// <param name="angularSpeed">公转角速度（度/秒，正=逆时针，负=顺时针）</param>
        /// <param name="outwardSpeed">径向向外速度</param>
        public void StartOrbitalMotion(Vector3 center, float angularSpeed, float outwardSpeed)
        {
            orbitalCenter = center;
            orbitalAngularSpeed = angularSpeed;
            orbitalOutwardSpeed = outwardSpeed;
            isInOrbitalMode = true;
            Log.Debug($"丝球启动公转模式 - 圆心: {center}, 角速度: {angularSpeed}°/s, 向外速度: {outwardSpeed}");
        }

        /// <summary>
        /// 停止公转运动
        /// </summary>
        public void StopOrbitalMotion()
        {
            isInOrbitalMode = false;
            orbitalAngularSpeed = 0f;
            orbitalOutwardSpeed = 0f;
        }

        /// <summary>
        /// 更新公转运动（每帧调用）
        /// 实现螺旋向外效果：径向速度 + 切向速度的组合
        /// </summary>
        private void UpdateOrbitalMotion()
        {
            if (rb2d == null) return;

            // 计算从圆心指向丝球的方向（径向）
            Vector2 radialDir = ((Vector2)transform.position - (Vector2)orbitalCenter).normalized;

            // 计算切向方向（垂直于径向，根据角速度符号决定方向）
            // 正角速度 = 逆时针 = 切向向量为 (-radialDir.y, radialDir.x)
            // 负角速度 = 顺时针 = 切向向量为 (radialDir.y, -radialDir.x)
            Vector2 tangentDir = new Vector2(-radialDir.y, radialDir.x);

            // 计算当前距离，用于调整切向速度（距离越远，切向线速度需要越大才能保持角速度）
            float distance = Vector2.Distance(transform.position, orbitalCenter);

            // 切向线速度 = 角速度(rad/s) * 半径
            // 将角速度从度转换为弧度：angularSpeed * Mathf.Deg2Rad
            float tangentSpeed = orbitalAngularSpeed * Mathf.Deg2Rad * distance;

            // 组合速度：径向向外 + 切向旋转
            Vector2 velocity = radialDir * orbitalOutwardSpeed + tangentDir * tangentSpeed;

            rb2d.linearVelocity = velocity;
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
            var forwarders = GetComponentsInChildren<MemorySilkBallCollisionForwarder>();
            foreach (var forwarder in forwarders)
            {
                forwarder.enabled = true;
            }
        }

        /// <summary>
        /// 初始化反向加速度模式（设置初始向外速度）
        /// </summary>
        public void InitReverseAcceleration()
        {
            isInReverseAccelMode = true;
            hasReversedDirection = false;

            // 计算初始向外方向（从圆心指向当前位置）
            Vector2 outwardDirection = ((Vector2)transform.position - (Vector2)reverseAccelCenter).normalized;

            if (rb2d != null)
            {
                // 设置初始向外速度
                rb2d.linearVelocity = outwardDirection * initialOutwardSpeed;
                rb2d.gravityScale = 0f;
            }
        }

        /// <summary>
        /// 配置反向加速度参数
        /// </summary>
        /// <param name="center">加速度指向的圆心位置</param>
        /// <param name="accel">加速度值</param>
        /// <param name="outSpeed">初始向外速度</param>
        /// <param name="inSpeed">最大向内速度</param>
        /// <param name="duration">最大持续时间</param>
        public void ConfigureReverseAcceleration(Vector3 center, float accel = 20f, float outSpeed = 15f, float inSpeed = 25f, float duration = 10f)
        {
            reverseAccelCenter = center;
            reverseAccelValue = accel;
            initialOutwardSpeed = outSpeed;
            maxInwardSpeed = inSpeed;
            reverseAccelDuration = duration;
        }

        /// <summary>
        /// 启动反向加速度模式（外部调用）
        /// </summary>
        public void StartReverseAccelerationMode()
        {
            if (controlFSM != null)
            {
                controlFSM.SendEvent("REVERSE_ACCEL");
            }
        }

        /// <summary>
        /// 检查是否处于反向加速度模式
        /// </summary>
        public bool IsInReverseAccelMode => isInReverseAccelMode;

        /// <summary>
        /// 检查速度是否已反转方向
        /// </summary>
        public bool HasReversedDirection => hasReversedDirection;

        /// <summary>
        /// 标记速度已反转方向
        /// </summary>
        public void MarkDirectionReversed()
        {
            hasReversedDirection = true;
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
            var collisionBox = otherObject.GetComponent<MemoryBigSilkBallCollisionBox>();
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

                // 如果处于保护时间内，忽略玩家碰撞
                if (isProtected)
                {
                    return;
                }


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
    internal class MemorySilkBallCollisionForwarder : MonoBehaviour
    {
        public MemorySilkBallBehavior? parent;

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
