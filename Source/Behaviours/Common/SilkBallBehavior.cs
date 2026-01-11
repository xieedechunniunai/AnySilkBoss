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

namespace AnySilkBoss.Source.Behaviours.Common
{
    /// <summary>
    /// 统一丝球Behavior - 管理单个丝球的行为和FSM
    /// 合并了普通版和梦境版的所有功能
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
        private float _protectionTimer = 0f;      // 保护时间计时器（替代协程）
        public bool canBeAbsorbed = false;        // 是否可以被大丝球吸收（仅吸收阶段的小丝球为true）
        private bool _delayDamageActivation = true;
        public bool triggerBlastOnDestroy = false; // 是否在销毁时触发 Blast 攻击
        public bool canBeClearedByAttack = true;  // 是否可被玩家攻击清除（Reaper 护符功能）

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
        
        // 新增：缓存关键GameObject引用，避免重复查找
        private FsmGameObject? heroGameObjectVar;    // 玩家 GameObject 缓存
        private FsmGameObject? spriteSilkObjVar;     // Sprite Silk 子物体
        private FsmGameObject? glowObjVar;           // Glow 子物体
        private FsmObject? damageHeroComponentVar;   // DamageHero 组件缓存（用于 EnableBehaviour）
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
            // 处理保护时间计时（替代协程）
            if (_protectionTimer > 0f)
            {
                _protectionTimer -= Time.deltaTime;
                if (_protectionTimer <= 0f)
                {
                    isProtected = false;
                    // 保护时间结束，如果不是穿墙丝球则恢复默认层级
                    if (!ignoreWallCollision)
                    {
                        UpdateRenderOrder(false);
                    }
                }
            }

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
        /// 获取组件引用（优化版本：使用 SilkBallManager 的静态缓存）
        /// </summary>
        private void GetComponentReferences()
        {
            // 从 SilkBallManager 的静态缓存获取管理器引用（避免 GameObject.Find）
            if (silkBallManager == null || _blastManager == null)
            {
                var managerObj = SilkBallManager.CachedManagerObject;
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
                    Log.Warn("SilkBallManager.CachedManagerObject 为 null，无法获取管理器引用");
                }
            }

            // 从 SilkBallManager 的静态缓存获取原始 DamageHero 引用（避免重复查找）
            if (originalDamageHero == null)
            {
                originalDamageHero = SilkBallManager.CachedOriginalDamageHero;
            }

            // 从 SilkBallManager 的静态缓存获取玩家引用
            if (playerTransform == null)
            {
                playerTransform = SilkBallManager.CachedHeroTransform;
                if (playerTransform == null)
                {
                    Log.Warn("SilkBallManager.CachedHeroTransform 为 null");
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
                }
            }

            // 获取 Glow 子物体引用（只在第一次调用时）
            if (Glow == null)
            {
                Glow = transform.Find("Glow");
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

            // 初始化 FSM 变量值（一次性，不再重复获取）
            InitializeFsmVariableValues();

            // 添加并永久启用碰撞转发器
            SetupCollisionForwarder();

            // 添加 EventRegister 组件（用于全局事件广播）
            SetupEventRegisters();

            // 确保初始状态正确
            isActive = false;
            _isAvailable = true;
        }

        /// <summary>
        /// 初始化 FSM 变量的值（一次性，从缓存中获取引用）
        /// </summary>
        private void InitializeFsmVariableValues()
        {
            if (heroGameObjectVar != null && playerTransform != null)
            {
                heroGameObjectVar.Value = playerTransform.gameObject;
            }
            
            if (spriteSilkObjVar != null && spriteSilk != null)
            {
                spriteSilkObjVar.Value = spriteSilk.gameObject;
            }
            
            if (glowObjVar != null && Glow != null)
            {
                glowObjVar.Value = Glow.gameObject;
            }
            
            if (damageHeroComponentVar != null && damageHero != null)
            {
                // 缓存 DamageHero 组件本身（用于 EnableBehaviour）
                damageHeroComponentVar.Value = damageHero;
            }
        }

        /// <summary>
        /// 设置碰撞转发器（永久启用，不再关闭）
        /// </summary>
        private void SetupCollisionForwarder()
        {
            if (spriteSilk == null) return;

            var forwarder = spriteSilk.GetComponent<SilkBallCollisionForwarder>();
            if (forwarder == null)
            {
                forwarder = spriteSilk.gameObject.AddComponent<SilkBallCollisionForwarder>();
            }
            forwarder.parent = this;
            forwarder.enabled = true;  // 永久启用，依赖 HandleCollision 中的 isActive 判断
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
        public void PrepareForUse(Vector3 spawnPosition, float acceleration = 30f, float maxSpeed = 20f, float chaseTime = 6f, float scale = 1f, bool enableRotation = true, Transform? customTarget = null, bool ignoreWall = false, bool delayDamageActivation = true, bool canBeClearedByAttack = true)
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

            // 重置状态（会将 canBeClearedByAttack 重置为 true）
            ResetState();

            // 在 ResetState 之后设置 canBeClearedByAttack，避免被重置覆盖
            this.canBeClearedByAttack = canBeClearedByAttack;

            // 标记为不可用（正在使用中）
            _isAvailable = false;
            isActive = true;

            // 穿墙丝球：设置 MeshRenderer 的 sortingOrder 让其显示在墙体前面
            UpdateRenderOrder(ignoreWall);

            // ⚠️ 关键修复：如果不需要延迟激活伤害，立即启用 DamageHero
            if (!_delayDamageActivation && damageHero != null)
            {
                damageHero.enabled = true;
            }

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

        /// <summary>
        /// 更新渲染顺序（穿墙丝球显示在墙体前面）
        /// </summary>
        private void UpdateRenderOrder(bool ignoreWall)
        {
            if (spriteSilk == null) return;

            var meshRenderer = spriteSilk.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                if (ignoreWall)
                {
                    // 穿墙丝球：设置较高的 sortingOrder 显示在墙体前面
                    meshRenderer.sortingOrder = 10;
                }
                else
                {
                    // 普通丝球：恢复默认 sortingOrder
                    meshRenderer.sortingOrder = 0;
                }
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
            canBeClearedByAttack = true;  // 重置为默认可被攻击清除

            // 重置速度
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.gravityScale = 0f;
            }

            // 禁用伤害组件（延迟激活由 Prepare 状态中的 ActivateGameObjectDelay 处理）
            if (damageHero != null)
            {
                damageHero.enabled = false;
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

            controlFSM.Fsm.HandleFixedUpdate = true;
            controlFSM.AddEventHandlerComponents();

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

            // GameObject 变量 - 用于存储追踪目标和缓存引用
            targetGameObjectVar = new FsmGameObject("Target") { Value = null };
            heroGameObjectVar = new FsmGameObject("HeroGameObject") { Value = null };
            spriteSilkObjVar = new FsmGameObject("SpriteSilkObj") { Value = null };
            glowObjVar = new FsmGameObject("GlowObj") { Value = null };
            
            controlFSM.FsmVariables.GameObjectVariables = new FsmGameObject[] 
            { 
                targetGameObjectVar, 
                heroGameObjectVar, 
                spriteSilkObjVar, 
                glowObjVar
            };

            // Object 变量 - 用于存储组件引用
            damageHeroComponentVar = new FsmObject("DamageHeroComponent") { Value = null };
            controlFSM.FsmVariables.ObjectVariables = new FsmObject[] { damageHeroComponentVar };

            controlFSM.FsmVariables.Init();
        }
        #endregion

        #region 添加状态动作
        private void AddInitActions(FsmState initState)
        {
            // Init状态：首次创建后立即进入Idle，无需等待
            initState.Actions = new FsmStateAction[] { };
        }

        private void AddIdleActions(FsmState idleState)
        {
            // Idle 状态：对象池待命状态，无限等待PREPARE事件
            idleState.Actions = new FsmStateAction[] { };
        }

        private void AddPrepareActions(FsmState prepareState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 设置 Ready = false（准备中）
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

            // 4. 【关键】延迟 0.15s 后启用 DamageHero 组件
            actions.Add(new EnableBehaviourDelay
            {
                behaviour = damageHeroComponentVar,
                enable = new FsmBool(true),
                delay = new FsmFloat(0.15f),
                resetOnExit = false
            });

            // 5. 设置 Ready = true（准备完成）
            actions.Add(new SetBoolValue
            {
                boolVariable = readyVar,
                boolValue = new FsmBool(true)
            });

            // 6. 激活 Sprite Silk
            actions.Add(new ActivateGameObject
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    GameObject = spriteSilkObjVar
                },
                activate = new FsmBool(true),
                recursive = false,
                resetOnExit = false,
                everyFrame = false
            });

            // 7. 激活 Glow
            if (glowObjVar != null)
            {
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = glowObjVar
                    },
                    activate = new FsmBool(true),
                    recursive = false,
                    resetOnExit = false,
                    everyFrame = false
                });
            }

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
            var enableDamage = new EnableBehaviourDelay
            {
                behaviour = damageHeroComponentVar,
                enable = new FsmBool(true),
                delay = new FsmFloat(0.15f),
                resetOnExit = false
            };
            // 使用通用的 ChaseTargetAction
            var chaseAction = new ChaseTargetAction
            {
                targetGameObject = targetGameObjectVar,
                acceleration = accelerationVar ?? new FsmFloat { Value = acceleration },
                maxSpeed = maxSpeedVar ?? new FsmFloat { Value = maxSpeed },
                useRigidbody = new FsmBool { Value = true },
                chaseTime = new FsmFloat { Value = 0f },
                reachDistance = new FsmFloat { Value = 0.1f }
            };

            // 超时等待
            var waitAction = new Wait
            {
                time = new FsmFloat(chaseTime),
                finishEvent = FsmEvent.Finished
            };

            // 攻击检测已移至 HandleCollision 方法中统一处理
            chaseState.Actions = new FsmStateAction[] { startChaseAction, enableDamage, chaseAction, waitAction };
        }

        private void AddDisperseActions(FsmState disperseState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 触发 Blast（如需）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("TriggerBlastIfNeeded") { Value = "TriggerBlastIfNeeded" },
                parameters = new FsmVar[0]
            });

            // 2. 禁用 DamageHero 组件
            actions.Add(new EnableBehaviourDelay
            {
                behaviour = damageHeroComponentVar,
                enable = new FsmBool(false),
                delay = new FsmFloat(0f),
                resetOnExit = false
            });

            // 3. 隐藏 Sprite Silk
            actions.Add(new ActivateGameObject
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    GameObject = spriteSilkObjVar
                },
                activate = new FsmBool(false),
                recursive = false,
                resetOnExit = false,
                everyFrame = false
            });

            // 4. 隐藏 Glow
            if (glowObjVar != null)
            {
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = glowObjVar
                    },
                    activate = new FsmBool(false),
                    recursive = false,
                    resetOnExit = false,
                    everyFrame = false
                });
            }

            // 5. 停止追踪
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            });

            // 6. 使用 DecelerateV2 减速
            actions.Add(new DecelerateV2
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                deceleration = new FsmFloat(0.8f),
                brakeOnExit = true
            });

            // 7. 播放消散粒子
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PlayCollectParticle") { Value = "PlayCollectParticle" },
                parameters = new FsmVar[0]
            });

            // 8. 播放 Get Silk 音效
            if (silkBallManager?.GetSilkAudioTable != null && silkBallManager?.GetSilkAudioPlayerPrefab != null)
            {
                actions.Add(new PlayRandomAudioClipTable
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
                });
            }

            // 9. 等待粒子播放完毕
            actions.Add(new Wait
            {
                time = new FsmFloat(0.6f),
                finishEvent = FsmEvent.Finished
            });

            disperseState.Actions = actions.ToArray();
        }

        private void AddHitHeroDisperseActions(FsmState hitHeroDisperseState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 触发 Blast（如需）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("TriggerBlastIfNeeded") { Value = "TriggerBlastIfNeeded" },
                parameters = new FsmVar[0]
            });

            // 2. 延迟 0.03s 后禁用 DamageHero 组件
            actions.Add(new EnableBehaviourDelay
            {
                behaviour = damageHeroComponentVar,
                enable = new FsmBool(false),
                delay = new FsmFloat(0.03f),
                resetOnExit = false
            });

            // 3. 延迟隐藏 Sprite Silk
            actions.Add(new ActivateGameObjectDelay
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    GameObject = spriteSilkObjVar
                },
                activate = new FsmBool(false),
                delay = new FsmFloat(0.03f),
                resetOnExit = false
            });

            // 4. 延迟隐藏 Glow
            if (glowObjVar != null)
            {
                actions.Add(new ActivateGameObjectDelay
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = glowObjVar
                    },
                    activate = new FsmBool(false),
                    delay = new FsmFloat(0.03f),
                    resetOnExit = false
                });
            }

            // 5. 停止追踪
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            });

            // 6. 使用 DecelerateV2 减速
            actions.Add(new DecelerateV2
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                deceleration = new FsmFloat(0.8f),
                brakeOnExit = true
            });

            // 7. 播放消散粒子
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PlayCollectParticle") { Value = "PlayCollectParticle" },
                parameters = new FsmVar[0]
            });

            // 8. 播放 Get Silk 音效
            if (silkBallManager?.GetSilkAudioTable != null && silkBallManager?.GetSilkAudioPlayerPrefab != null)
            {
                actions.Add(new PlayRandomAudioClipTable
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
                });
            }

            // 9. 等待粒子播放完毕
            actions.Add(new Wait
            {
                time = new FsmFloat(0.6f),
                finishEvent = FsmEvent.Finished
            });

            hitHeroDisperseState.Actions = actions.ToArray();
        }

        private void AddDisappearActions(FsmState disappearState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 禁用 DamageHero 组件
            actions.Add(new EnableBehaviourDelay
            {
                behaviour = damageHeroComponentVar,
                enable = new FsmBool(false),
                delay = new FsmFloat(0f),
                resetOnExit = false
            });

            // 2. 停止追踪
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopChase") { Value = "StopChase" },
                parameters = new FsmVar[0]
            });

            // 3. 使用 DecelerateV2 减速
            actions.Add(new DecelerateV2
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                deceleration = new FsmFloat(0.8f),
                brakeOnExit = true
            });

            // 4. 播放消散粒子
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PlayCollectParticle") { Value = "PlayCollectParticle" },
                parameters = new FsmVar[0]
            });

            // 5. 播放消失动画
            if (spriteSilkObjVar != null)
            {
                actions.Add(new Tk2dPlayAnimationWithEvents
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = spriteSilkObjVar
                    },
                    clipName = new FsmString("Bundle Disappear") { Value = "Bundle Disappear" },
                    animationTriggerEvent = null,
                    animationCompleteEvent = null
                });
            }

            // 6. 等待动画和粒子播放完毕
            actions.Add(new Wait
            {
                time = new FsmFloat(0.9f),
                finishEvent = FsmEvent.Finished
            });

            disappearState.Actions = actions.ToArray();
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

            hasGravityState.Actions = new FsmStateAction[] { stopChaseAction, waitAction };
        }

        private void AddReverseAccelerationActions(FsmState reverseAccelState)
        {
            // 初始化反向加速度模式
            var initReverseAccelAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("InitReverseAcceleration") { Value = "InitReverseAcceleration" },
                parameters = new FsmVar[0]
            };

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

            // 超时等待
            var waitAction = new Wait
            {
                time = new FsmFloat(reverseAccelDuration),
                finishEvent = FsmEvent.Finished
            };

            // 攻击检测已移至 HandleCollision 方法中统一处理
            reverseAccelState.Actions = new FsmStateAction[] { initReverseAccelAction, reverseAccelAction, waitAction };
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
                var managerObj = SilkBallManager.CachedManagerObject;
                if (managerObj != null)
                {
                    _blastManager = managerObj.GetComponent<FWBlastManager>();
                }
            }
            
            Log.Debug($"[SilkBall] 触发 Blast 攻击");
            if (_blastManager != null && _blastManager.IsInitialized)
            {
                _blastManager.SpawnBombBlast(transform.position);
                Log.Debug($"[SilkBall] 触发 Blast at {transform.position}");
            }
            else
            {
                Log.Warn("[SilkBall] FWBlastManager 未就绪，无法触发 Blast");
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
            if (customTarget != null && customTarget)
            {
                target = customTarget.gameObject;
            }
            else
            {
                if (heroGameObjectVar != null && heroGameObjectVar.Value != null && heroGameObjectVar.Value)
                {
                    target = heroGameObjectVar.Value;
                }
                else if (playerTransform != null && playerTransform)
                {
                    target = playerTransform.gameObject;
                }
                else
                {
                    Log.Warn("[SilkBallBehavior] StartChase: 玩家引用为空，无法追踪");
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
            canBeClearedByAttack = true;  // 重置为默认可被攻击清除

            // 重置速度
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.gravityScale = 0f;
            }

            // 回收时禁用伤害组件
            if (damageHero != null)
            {
                damageHero.enabled = false;
            }

            // 清空自定义目标
            customTarget = null;
            
            // 隐藏所有子物体
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.SetActive(false);
            }
            if (Glow != null)
            {
                Glow.gameObject.SetActive(false);
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

            // 重置到 Idle 状态
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

        private IEnumerator RecycleToPoolWithZTransitionCoroutine()
        {
            isChasing = false;
            canBeAbsorbed = false;

            if (damageHero != null)
            {
                damageHero.enabled = false;
            }

            if (mainCollider != null)
            {
                mainCollider.enabled = false;
            }

            yield return null;

            RecycleToPool();
        }

        /// <summary>
        /// 设置初始速度
        /// </summary>
        public void SetInitialVelocity(Vector2 velocity)
        {
            if (rb2d != null)
            {
                rb2d.linearVelocity = velocity;
            }
        }

        /// <summary>
        /// 设置重力缩放
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
        public void ApplyDelayedBoostTowardsHero(float delay, float acceleration, float duration = 2f)
        {
            StartCoroutine(DelayedBoostCoroutine(delay, acceleration, duration));
        }

        private IEnumerator DelayedBoostCoroutine(float delay, float acceleration, float duration)
        {
            yield return new WaitForSeconds(delay);

            if (!isActive || rb2d == null) yield break;

            Vector2 direction = Vector2.zero;
            if (playerTransform != null)
            {
                direction = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
            }

            if (direction == Vector2.zero) yield break;

            // 逐渐减速到接近0
            while (rb2d.linearVelocity.magnitude > 1f)
            {
                rb2d.linearVelocity *= 0.9f;
                yield return new WaitForSeconds(0.018f);
            }
            
            rb2d.linearVelocity = direction * 8f;
            
            const float maxSpeed = 30f;
            float elapsed = 0f;
            while (elapsed < duration && isActive && rb2d != null)
            {
                rb2d.linearVelocity += direction * acceleration * Time.deltaTime;

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
        }

        /// <summary>
        /// 启动公转运动
        /// </summary>
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

        private void UpdateOrbitalMotion()
        {
            if (rb2d == null) return;

            Vector2 radialDir = ((Vector2)transform.position - (Vector2)orbitalCenter).normalized;
            Vector2 tangentDir = new Vector2(-radialDir.y, radialDir.x);
            float distance = Vector2.Distance(transform.position, orbitalCenter);
            float tangentSpeed = orbitalAngularSpeed * Mathf.Deg2Rad * distance;
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
        }

        /// <summary>
        /// 初始化反向加速度模式
        /// </summary>
        public void InitReverseAcceleration()
        {
            isInReverseAccelMode = true;
            hasReversedDirection = false;

            Vector2 outwardDirection = ((Vector2)transform.position - (Vector2)reverseAccelCenter).normalized;

            if (rb2d != null)
            {
                rb2d.linearVelocity = outwardDirection * initialOutwardSpeed;
                rb2d.gravityScale = 0f;
            }
        }

        /// <summary>
        /// 配置反向加速度参数
        /// </summary>
        public void ConfigureReverseAcceleration(Vector3 center, float accel = 20f, float outSpeed = 15f, float inSpeed = 25f, float duration = 10f)
        {
            reverseAccelCenter = center;
            reverseAccelValue = accel;
            initialOutwardSpeed = outSpeed;
            maxInwardSpeed = inSpeed;
            reverseAccelDuration = duration;
        }

        /// <summary>
        /// 启动反向加速度模式
        /// </summary>
        public void StartReverseAccelerationMode()
        {
            if (controlFSM != null)
            {
                controlFSM.SendEvent("REVERSE_ACCEL");
            }
        }

        public bool IsInReverseAccelMode => isInReverseAccelMode;
        public bool HasReversedDirection => hasReversedDirection;

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
                return;
            }

            // 检查大丝球碰撞箱（追踪大丝球的小丝球会被吸收）
            var collisionBox = otherObject.GetComponent<Memory.MemoryBigSilkBallCollisionBox>();
            if (collisionBox != null)
            {
                return;
            }

            // 检查墙壁碰撞
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            int defaultLayer = LayerMask.NameToLayer("Default");

            if (otherObject.layer == terrainLayer || otherObject.layer == defaultLayer)
            {
                if (ignoreWallCollision)
                {
                    return;
                }

                if (isProtected)
                {
                    return;
                }

                controlFSM.SendEvent("HIT WALL");
                return;
            }

            // 检查玩家碰撞
            int heroBoxLayer = LayerMask.NameToLayer("Hero Box");

            if (otherObject.layer == heroBoxLayer)
            {
                if (isProtected)
                {
                    return;
                }

                controlFSM.SendEvent("HIT HERO");
                return;
            }

            // 检查玩家攻击碰撞（Reaper 护符功能）
            // 只在 Chase Hero 或 Reverse Acceleration 状态下检测
            if (isChasing || isInReverseAccelMode)
            {
                // 检查是否为玩家攻击（Tag: "Nail Attack" 或 Layer: 17）
                if (otherObject.CompareTag("Nail Attack") || otherObject.layer == 17)
                {
                    // 检查是否满足清除条件：装备 Reaper 护符且丝球可被攻击清除
                    if (SilkBallManager.IsReaperCrestEquipped && canBeClearedByAttack)
                    {
                        controlFSM.SendEvent("HIT WALL");  // 触发消散
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 启动保护时间
        /// </summary>
        public void StartProtectionTime(float duration)
        {
            isProtected = true;
            _protectionTimer = duration;
            
            // 保护期间设置高层级显示（显示在墙体前面）
            UpdateRenderOrder(true);
        }
        #endregion
    }

    #region 碰撞转发器
    /// <summary>
    /// 统一碰撞转发器 - 将子物体的碰撞事件转发给父 SilkBallBehavior
    /// </summary>
    internal class SilkBallCollisionForwarder : MonoBehaviour
    {
        public SilkBallBehavior? parent;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (parent != null)
            {
                parent.HandleCollision(other.gameObject);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (parent != null)
            {
                parent.HandleCollision(collision.gameObject);
            }
        }
    }
    #endregion
}
