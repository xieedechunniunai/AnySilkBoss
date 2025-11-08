using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using System;

namespace AnySilkBoss.Source.Behaviours
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
        #endregion

        #region 组件引用
        private PlayMakerFSM? controlFSM;          // Control FSM
        private Rigidbody2D? rb2d;                 // 刚体组件
        private Transform? playerTransform;        // 玩家Transform
        private DamageHero? damageHero;            // 伤害组件

        // 管理器引用
        private Managers.SilkBallManager? silkBallManager;  // 丝球管理器
        private DamageHero? originalDamageHero;            // 原始 DamageHero 组件引用

        // 子物体引用
        private Transform? spriteSilk;             // Sprite Silk 子物体
        private CircleCollider2D? mainCollider;    // 主碰撞器
        private ParticleSystem? ptCollect;         // 快速消散粒子
        private ParticleSystem? ptDisappear;       // 缓慢消失粒子
        #endregion

        #region FSM 变量引用
        private FsmBool? readyVar;
        private FsmFloat? accelerationVar;
        private FsmFloat? maxSpeedVar;
        #endregion

        #region 事件引用
        private FsmEvent? releaseEvent;
        private FsmEvent? hitWallEvent;
        private FsmEvent? hitHeroEvent;
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
            // 首先获取 SilkBallManager 和原始 DamageHero 引用
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                silkBallManager = managerObj.GetComponent<Managers.SilkBallManager>();
                if (silkBallManager != null)
                {
                    originalDamageHero = silkBallManager.OriginalDamageHero;
                    if (originalDamageHero == null)
                    {
                        Log.Warn("SilkBallManager 中的原始 DamageHero 为 null");
                    }
                }
                else
                {
                    Log.Warn("AnySilkBossManager 上未找到 SilkBallManager 组件");
                }
            }
            else
            {
                Log.Warn("未找到 AnySilkBossManager GameObject");
            }

            // 获取或添加 Rigidbody2D
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

            // 获取子物体引用
            spriteSilk = transform.Find("Sprite Silk");
            if (spriteSilk != null)
            {
                mainCollider = spriteSilk.GetComponent<CircleCollider2D>();
                if (mainCollider != null)
                {
                    mainCollider.isTrigger = true;
                    mainCollider.enabled = true;
                    // 降低50%碰撞箱大小
                    mainCollider.radius *= 0.5f;
                }

                // 添加碰撞转发器
                var collisionForwarder = spriteSilk.GetComponent<SilkBallCollisionForwarder>();
                if (collisionForwarder == null)
                {
                    collisionForwarder = spriteSilk.gameObject.AddComponent<SilkBallCollisionForwarder>();
                    collisionForwarder.parent = this;
                }
            }

            // 获取粒子系统并设置大小为两倍
            var ptCollectTransform = transform.Find("Pt Collect");
            if (ptCollectTransform != null)
            {
                ptCollect = ptCollectTransform.GetComponent<ParticleSystem>();
                if (ptCollect != null)
                {
                    ptCollectTransform.localScale = Vector3.one * 2f;
                }
            }

            var ptDisappearTransform = transform.Find("Pt Disappear");
            if (ptDisappearTransform != null)
            {
                ptDisappear = ptDisappearTransform.GetComponent<ParticleSystem>();
                if (ptDisappear != null)
                {
                    ptDisappearTransform.localScale = Vector3.one * 2f;
                }
            }

            // 在 Sprite Silk 子物体上添加/配置 DamageHero 组件
            if (spriteSilk != null)
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
            }

            // 查找玩家
            var heroController = FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                playerTransform = heroController.transform;
            }
        }

        /// <summary>
        /// 初始化丝球（从管理器调用）
        /// </summary>
        public void Initialize(Vector3 spawnPosition, float acceleration = 30f, float maxSpeed = 20f, float chaseTime = 6f, float scale = 1f, bool enableRotation = true)
        {
            transform.position = spawnPosition;

            // 设置参数
            this.acceleration = acceleration;
            this.maxSpeed = maxSpeed;
            this.chaseTime = chaseTime;
            this.scale = scale;
            this.enableRotation = enableRotation;

            // 应用缩放
            ApplyScale();

            // 设置层级为 Enemy Attack
            gameObject.layer = LayerMask.NameToLayer("Enemy Attack");
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.layer = LayerMask.NameToLayer("Enemy Attack");
            }


            // 创建 FSM
            CreateControlFSM();

            // 激活
            isActive = true;

        }

        /// <summary>
        /// 应用整体缩放
        /// </summary>
        private void ApplyScale()
        {
            // 缩放根物体
            transform.localScale = Vector3.one * scale;

            // 如果碰撞器已经缩小了50%，需要调整回来再应用scale
            if (mainCollider != null)
            {
                // 碰撞器大小已经在 Awake 中缩小了50%，这里再应用scale
                mainCollider.radius *= scale;
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
            var prepareState = CreatePrepareState();
            var chaseState = CreateChaseHeroState();
            var disperseState = CreateDisperseState();
            var hitHeroDisperseState = CreateHitHeroDisperseState();
            var disappearState = CreateDisappearState();
            var destroyState = CreateDestroyState();
            var hasGravityState = CreateHasGravityState();

            // 设置状态到 FSM（必须在最早设置）
            controlFSM.Fsm.States = new FsmState[]
            {
                initState,
                prepareState,
                chaseState,
                disperseState,
                hitHeroDisperseState,
                disappearState,
                destroyState,
                hasGravityState
            };



            // 注册所有事件
            RegisterFSMEvents();

            // 创建 FSM 变量
            CreateFSMVariables();

            // 添加状态动作
            AddInitActions(initState);
            AddPrepareActions(prepareState);
            AddChaseHeroActions(chaseState);
            AddDisperseActions(disperseState);
            AddHitHeroDisperseActions(hitHeroDisperseState);
            AddDisappearActions(disappearState);
            AddDestroyActions(destroyState);
            AddHasGravityActions(hasGravityState);

            // 添加状态转换
            AddInitTransitions(initState, prepareState);
            AddPrepareTransitions(prepareState, chaseState);
            AddChaseHeroTransitions(chaseState, disperseState, hitHeroDisperseState, disappearState);
            AddDisperseTransitions(disperseState, destroyState);
            AddHitHeroDisperseTransitions(hitHeroDisperseState, destroyState);
            AddDisappearTransitions(disappearState, destroyState);
            AddHasGravityTransitions(hasGravityState, disperseState, hitHeroDisperseState);

            // 初始化 FSM 数据和事件
            controlFSM.Fsm.InitData();
            controlFSM.Fsm.InitEvents();

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
            Log.Info($"=== 丝球 Control FSM 创建完成，当前状态: {controlFSM.Fsm.ActiveStateName} ===");
        }

        #region FSM 事件和变量注册
        /// <summary>
        /// 注册 FSM 事件
        /// </summary>
        private void RegisterFSMEvents()
        {
            releaseEvent = FsmEvent.GetFsmEvent("SILK BALL RELEASE");
            hitWallEvent = FsmEvent.GetFsmEvent("HIT WALL");
            hitHeroEvent = FsmEvent.GetFsmEvent("HIT HERO");

            var events = controlFSM!.FsmEvents.ToList();
            if (!events.Contains(releaseEvent)) events.Add(releaseEvent);
            if (!events.Contains(hitWallEvent)) events.Add(hitWallEvent);
            if (!events.Contains(hitHeroEvent)) events.Add(hitHeroEvent);

            // 使用反射设置事件
            var fsmType = controlFSM.Fsm.GetType();
            var eventsField = fsmType.GetField("events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (eventsField != null)
            {
                eventsField.SetValue(controlFSM.Fsm, events.ToArray());
                Log.Info("FSM 事件注册完成");
            }
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
        }
        #endregion

        #region 创建状态
        private FsmState CreateInitState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Init",
                Description = "初始化状态"
            };
        }

        private FsmState CreatePrepareState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Prepare",
                Description = "准备状态，等待释放"
            };
        }

        private FsmState CreateChaseHeroState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Chase Hero",
                Description = "追逐玩家"
            };
        }

        private FsmState CreateDisperseState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Disperse",
                Description = "快速消散（碰到墙壁）"
            };
        }

        private FsmState CreateHitHeroDisperseState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Hit Hero Disperse",
                Description = "碰到玩家消散（保持伤害启用）"
            };
        }

        private FsmState CreateDisappearState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Disappear",
                Description = "缓慢消失"
            };
        }

        private FsmState CreateDestroyState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Destroy",
                Description = "销毁"
            };
        }

        private FsmState CreateHasGravityState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Has Gravity",
                Description = "有重力状态"
            };
        }
        #endregion

        #region 添加状态动作
        private void AddInitActions(FsmState initState)
        {
            var setBoolAction = new SetBoolValue
            {
                boolVariable = readyVar,
                boolValue = new FsmBool(false)
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            };

            // 创建 Init 音效动作
            FsmStateAction? initAudio = null;
            if (silkBallManager?.InitAudioTable != null && silkBallManager?.InitAudioPlayerPrefab != null)
            {
                initAudio = new PlayRandomAudioClipTable
                {
                    Table = silkBallManager.InitAudioTable,
                    AudioPlayerPrefab = silkBallManager.InitAudioPlayerPrefab,
                    SpawnPoint = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.UseOwner,
                        GameObject = new FsmGameObject { Value = gameObject }
                    },
                    SpawnPosition = new FsmVector3 { Value = Vector3.zero }
                };
            }

            // 添加动作：先设置变量，再播放音效，最后等待
            if (initAudio != null)
            {
                initState.Actions = new FsmStateAction[] { setBoolAction, initAudio, waitAction };
            }
            else
            {
                initState.Actions = new FsmStateAction[] { setBoolAction, waitAction };
            }
        }

        private void AddPrepareActions(FsmState prepareState)
        {
            // 速度归零
            var setVelocityAction = new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = new Vector2(0f, 0f), UseVariable = false },  // 必须初始化 vector 字段
                x = new FsmFloat { Value = 0f, UseVariable = false },
                y = new FsmFloat { Value = 0f, UseVariable = false },
                everyFrame = false
            };

            // 设置 Ready 为 true
            var setBoolAction = new SetBoolValue
            {
                boolVariable = readyVar,
                boolValue = new FsmBool(true)
            };

            // 等待释放事件（无限等待）
            var waitAction = new Wait
            {
                time = new FsmFloat(999f),
                finishEvent = FsmEvent.Finished
            };

            prepareState.Actions = new FsmStateAction[] { setVelocityAction, setBoolAction, waitAction };
        }

        private void AddChaseHeroActions(FsmState chaseState)
        {
            // 自定义追踪动作
            var chaseAction = new SilkBallChaseAction
            {
                silkBallBehavior = this,
                acceleration = accelerationVar,
                maxSpeed = maxSpeedVar
            };

            // 超时等待
            var waitAction = new Wait
            {
                time = new FsmFloat(chaseTime),
                finishEvent = FsmEvent.Finished
            };

            chaseState.Actions = new FsmStateAction[] { chaseAction, waitAction };
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
                    fsm = controlFSM.Fsm,
                    owner = controlFSM.gameObject
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
                    fsm = controlFSM.Fsm,
                    owner = controlFSM.gameObject
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

        private void AddDestroyActions(FsmState destroyState)
        {
            var destroyAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DestroySelf") { Value = "DestroySelf" },
                parameters = new FsmVar[0]
            };

            destroyState.Actions = new FsmStateAction[] { destroyAction };
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

        #region 添加状态转换
        private void AddInitTransitions(FsmState initState, FsmState prepareState)
        {
            initState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Prepare",
                    toFsmState = prepareState
                }
            };
        }

        private void AddPrepareTransitions(FsmState prepareState, FsmState chaseState)
        {
            prepareState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = releaseEvent,
                    toState = "Chase Hero",
                    toFsmState = chaseState
                }
            };
        }

        private void AddChaseHeroTransitions(FsmState chaseState, FsmState disperseState, FsmState hitHeroDisperseState, FsmState disappearState)
        {
            chaseState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Disappear",
                    toFsmState = disappearState
                },
                new FsmTransition
                {
                    FsmEvent = hitWallEvent,
                    toState = "Disperse",
                    toFsmState = disperseState
                },
                new FsmTransition
                {
                    FsmEvent = hitHeroEvent,
                    toState = "Hit Hero Disperse",
                    toFsmState = hitHeroDisperseState
                }
            };
        }

        private void AddDisperseTransitions(FsmState disperseState, FsmState destroyState)
        {
            disperseState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Destroy",
                    toFsmState = destroyState
                }
            };
        }

        private void AddHitHeroDisperseTransitions(FsmState hitHeroDisperseState, FsmState destroyState)
        {
            hitHeroDisperseState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Destroy",
                    toFsmState = destroyState
                }
            };
        }

        private void AddDisappearTransitions(FsmState disappearState, FsmState destroyState)
        {
            disappearState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Destroy",
                    toFsmState = destroyState
                }
            };
        }

        private void AddHasGravityTransitions(FsmState hasGravityState, FsmState disperseState, FsmState hitHeroDisperseState)
        {
            hasGravityState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = hitWallEvent,
                    toState = "Disperse",
                    toFsmState = disperseState
                },
                new FsmTransition
                {
                    FsmEvent = hitHeroEvent,
                    toState = "Hit Hero Disperse",
                    toFsmState = hitHeroDisperseState
                }
            };
        }
        #endregion


        #region 辅助方法（供FSM调用）
        /// <summary>
        /// 停止追踪
        /// </summary>
        public void StopChase()
        {
            isChasing = false;
            Debug.Log("停止追踪");
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
            Debug.Log("禁用伤害碰撞");
        }

        /// <summary>
        /// 禁用伤害并隐藏 Sprite Silk
        /// </summary>
        public void DisableDamageAndVisual()
        {
            // 禁用伤害
            DisableDamage();

            // 隐藏 Sprite Silk 子物体
            if (spriteSilk != null)
            {
                spriteSilk.gameObject.SetActive(false);
                Debug.Log("隐藏 Sprite Silk 子物体");
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
        /// 销毁自身
        /// </summary>
        public void DestroySelf()
        {
            // Debug.Log("销毁丝球");
            Destroy(gameObject);
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
            Log.Info($"丝球自转已{(enable ? "启用" : "禁用")}");
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

            // 检查墙壁碰撞（立即消散）
            int terrainLayer = LayerMask.NameToLayer("Terrain");
            int defaultLayer = LayerMask.NameToLayer("Default");

            if (otherObject.layer == terrainLayer || otherObject.layer == defaultLayer)
            {
                Debug.Log($"碰到墙壁: {otherObject.name}, 发送 HIT WALL 事件");
                controlFSM.SendEvent("HIT WALL");
                return;
            }

            // 检查玩家碰撞（Hero Box 图层）
            int heroBoxLayer = LayerMask.NameToLayer("Hero Box");
            
            if ( otherObject.layer == heroBoxLayer)
            {
                // Debug.Log($"碰到玩家: {otherObject.name}, 发送 HIT HERO 事件");
                controlFSM.SendEvent("HIT HERO");
                return;
            }
        }
        #endregion
    }

    #region 自定义 FSM Action
    /// <summary>
    /// 丝球追踪玩家的自定义Action
    /// </summary>
    internal class SilkBallChaseAction : FsmStateAction
    {
        public SilkBallBehavior? silkBallBehavior;
        public FsmFloat? acceleration;
        public FsmFloat? maxSpeed;

        private Rigidbody2D? rb2d;
        private Transform? playerTransform;

        public override void Reset()
        {
            silkBallBehavior = null;
            acceleration = 30f;
            maxSpeed = 20f;
        }

        public override void OnEnter()
        {
            if (silkBallBehavior == null)
            {
                Debug.Log("SilkBallChaseAction: silkBallBehavior 为 null");
                Finish();
                return;
            }

            rb2d = silkBallBehavior.GetComponent<Rigidbody2D>();
            var heroController = UnityEngine.Object.FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                playerTransform = heroController.transform;
            }

            if (rb2d == null || playerTransform == null)
            {
                Debug.Log("SilkBallChaseAction: Rigidbody2D 或 Player Transform 为 null");
                Finish();
                return;
            }

            silkBallBehavior.isChasing = true;
        }

        public override void OnUpdate()
        {
            if (rb2d == null || playerTransform == null || silkBallBehavior == null)
            {
                Finish();
                return;
            }

            if (!silkBallBehavior.isChasing)
            {
                Finish();
                return;
            }

            // 计算朝向玩家的方向
            Vector2 direction = (playerTransform.position - silkBallBehavior.transform.position).normalized;

            // 应用加速度
            float accel = acceleration != null ? acceleration.Value : 30f;
            Vector2 accelerationForce = direction * accel;
            rb2d.linearVelocity += accelerationForce * Time.deltaTime;

            // 限制最大速度
            float maxSpd = maxSpeed != null ? maxSpeed.Value : 20f;
            if (rb2d.linearVelocity.magnitude > maxSpd)
            {
                rb2d.linearVelocity = rb2d.linearVelocity.normalized * maxSpd;
            }
        }

        public override void OnExit()
        {
            if (silkBallBehavior != null)
            {
                silkBallBehavior.isChasing = false;
            }
        }
    }
    #endregion

    #region 碰撞转发器
    /// <summary>
    /// 碰撞转发器 - 将子物体的碰撞事件转发给父 SilkBallBehavior
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
