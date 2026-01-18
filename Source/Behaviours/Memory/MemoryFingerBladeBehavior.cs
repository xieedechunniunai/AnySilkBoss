using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using System.Linq;
using System;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Actions;
using GenericVariableExtension;
using AnySilkBoss.Source.Managers;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// Finger Blade控制Behavior - 管理单根Finger Blade的行为
    /// </summary>
    internal class MemoryFingerBladeBehavior : MonoBehaviour
    {
        [Header("Finger Blade配置")]
        public int bladeIndex = -1;              // 在手中的索引
        public string parentHandName = "";       // 父手部名称
        public MemoryHandControlBehavior? parentHand; // 父手部Behavior引用

        [Header("环绕参数")]
        private FsmFloat? _orbitRadiusVar;
        private FsmFloat? _orbitSpeedVar;
        private FsmFloat? _orbitOffsetVar;
        private FsmFloat? _trackTimeVar;         // Track Time 变量缓存
        private FsmFloat? _dashRotationOffsetVar;
        private FsmBool? _specialAttackVar;      // Special Attack 变量缓存
        private FsmVector3? _pinArraySlotTargetVar;

        private FsmBool? _dashReadyVar;
        private FsmBool? _ReadyVar;
        private FsmVector3? _dashOrbitTargetPosVar;
        private FsmFloat? _dashOrbitTargetRotationVar;

        // 私有变量
        private PlayMakerFSM? controlFSM;        // Control FSM
        private PlayMakerFSM? tinkFSM;           // Tink FSM(貌似没用)
        private Transform? playerTransform;      // 玩家Transform
                                                 // 运行时缓存：新增的FSM变量引用，确保动作字段绑定到同一实例
        private FWPinManager? _pinManager;
        private MeshRenderer? _meshRenderer;     // MeshRenderer缓存，用于控制显示层级

        // 事件引用缓存
        private FsmEvent? _orbitStartEvent;
        private FsmEvent? _shootEvent;
        private FsmEvent? _dashOrbitStartEvent;
        private FsmEvent? _pinArrayEnterEvent;
        private FsmEvent? _pinArrayAttackEvent;
        private FsmState? _shootState;
        private FsmState? _pinArrayMoveToSlotState;
        private FsmState? _pinArrayAimState;

        /// <summary>
        /// 初始化Finger Blade
        /// </summary>
        public void Initialize(int index, string handName, MemoryHandControlBehavior hand)
        {
            // 修改bladeIndex为全局唯一：Hand L(0-2), Hand R(3-5)
            int globalIndex = handName == "Hand L" ? index : index + 3;
            bladeIndex = globalIndex;
            parentHandName = handName;
            parentHand = hand;
            // 查找玩家
            var heroController = FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                playerTransform = heroController.transform;
            }
            // 获取FSM组件
            InitializeFSMs();
        }

        /// <summary>
        /// 初始化FSM组件
        /// </summary>
        private void InitializeFSMs()
        {
            PlayMakerFSM[] allFSMs = GetComponents<PlayMakerFSM>();

            foreach (PlayMakerFSM fsm in allFSMs)
            {
                if (fsm.FsmName == "Control")
                {
                    controlFSM = fsm;
                }
                else if (fsm.FsmName == "Tink")
                {
                    tinkFSM = fsm;
                }
            }
            _pinManager = FindFirstObjectByType<FWPinManager>();
            if (_pinManager == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 FWPinManager，Pin 发射功能将不可用");
            }
            if (controlFSM == null)
            {
                Log.Warn($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到Control FSM");
            }
            // 缓存 MeshRenderer 用于显示层级控制
            _meshRenderer = GetComponent<MeshRenderer>();
            if (_meshRenderer == null)
            {
                Log.Warn($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到 MeshRenderer，无法控制显示层级");
            }
            AddDynamicVariables();
            EnsureDashOrbitVariables();
            RegisterFingerBladeEvents();
            AddCustomStompStates();
            AddCustomSwipeStates();
            ModifyAnticPullState();  // 修改 Antic Pull 状态的等待时间
            ModifyAnticPauseState(); // 修改 Antic Pause 状态，添加二阶段追踪判断
            ModifyShootState();
            ModifyThunkState();      // 修改 Thunk 状态，重置 Damager layer
            InitializePinArraySpecialStates();
            // 添加环绕攻击全局转换
            AddOrbitGlobalTransition();
            AddDashOrbitGlobalTransition();
            // 统一初始化FSM（在所有状态和事件添加完成后）
            RelinkFingerBladeEventReferences();
        }

        private void InitializePinArraySpecialStates()
        {
            if (controlFSM == null) return;

            EnsurePinArrayVariables();
            RegisterPinArrayEvents();
            CreatePinArraySpecialStates();
            AddPinArrayGlobalTransition();
        }

        private void EnsurePinArrayVariables()
        {
            if (controlFSM == null) return;

            var vars = controlFSM.FsmVariables;
            var vector3Vars = vars.Vector3Variables.ToList();
            _pinArraySlotTargetVar = vector3Vars.FirstOrDefault(v => v.Name == "PinArray Slot Target");
            if (_pinArraySlotTargetVar == null)
            {
                _pinArraySlotTargetVar = new FsmVector3("PinArray Slot Target") { Value = Vector3.zero };
                vector3Vars.Add(_pinArraySlotTargetVar);
                vars.Vector3Variables = vector3Vars.ToArray();
                vars.Init();
            }
        }

        private void RegisterPinArrayEvents()
        {
            if (controlFSM == null) return;

            _pinArrayEnterEvent = FsmEvent.GetFsmEvent("PINARRAY_ENTER");
            _pinArrayAttackEvent = FsmEvent.GetFsmEvent("PINARRAY_ATTACK");

            var events = controlFSM.Fsm.Events.ToList();
            if (!events.Contains(_pinArrayEnterEvent)) events.Add(_pinArrayEnterEvent);
            if (!events.Contains(_pinArrayAttackEvent)) events.Add(_pinArrayAttackEvent);
            controlFSM.Fsm.Events = events.ToArray();
        }

        private void CreatePinArraySpecialStates()
        {
            if (controlFSM == null) return;

            _pinArrayMoveToSlotState = GetOrCreateState(controlFSM, "PinArray MoveToSlot", $"Finger Blade {bladeIndex} PinArray MoveToSlot");
            _pinArrayAimState = GetOrCreateState(controlFSM, "PinArray Aim", $"Finger Blade {bladeIndex} PinArray Aim");

            AddPinArrayMoveToSlotActions(_pinArrayMoveToSlotState);
            AddPinArrayAimActions(_pinArrayAimState);

            SetFinishedTransition(_pinArrayMoveToSlotState, _pinArrayAimState);

            var anticPullState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Antic Pull");
            if (anticPullState != null && _pinArrayAttackEvent != null)
            {
                _pinArrayAimState.Transitions = new FsmTransition[]
                {
                    CreateTransition(_pinArrayAttackEvent, anticPullState)
                };
            }
        }

        private void AddPinArrayMoveToSlotActions(FsmState state)
        {
            if (controlFSM == null) return;

            var zeroVelocityAction = new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = Vector2.zero, UseVariable = false },
                x = new FsmFloat { Value = 0f, UseVariable = false },
                y = new FsmFloat { Value = 0f, UseVariable = false },
                everyFrame = false
            };

            // 使用 AnimatePositionTo
            var moveAction = new AnimatePositionTo
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                toValue = _pinArraySlotTargetVar,
                localSpace = false,
                time = new FsmFloat(0.6f), // 最大时间作为超时保护
                speed = new FsmFloat(10f), // 使用固定速度
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(0.4f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[]
            {
                zeroVelocityAction,
                moveAction,
                waitAction
            };
        }
        private void AddPinArrayAimActions(FsmState state)
        {
            var aimAction = new CallMethod
            {
                behaviour = this,
                methodName = "UpdatePinArrayAim",
                parameters = new FsmVar[0],
                everyFrame = true
            };

            state.Actions = new FsmStateAction[]
            {
                aimAction
            };
        }

        private void AddPinArrayGlobalTransition()
        {
            if (controlFSM == null) return;
            if (_pinArrayEnterEvent == null || _pinArrayMoveToSlotState == null) return;

            var globalTrans = controlFSM.Fsm.GlobalTransitions.ToList();
            bool exists = globalTrans.Any(t => t.FsmEvent == _pinArrayEnterEvent && (t.toState == _pinArrayMoveToSlotState.Name || t.toFsmState == _pinArrayMoveToSlotState));
            if (!exists)
            {
                globalTrans.Add(CreateTransition(_pinArrayEnterEvent, _pinArrayMoveToSlotState));
                controlFSM.Fsm.GlobalTransitions = globalTrans.ToArray();
            }
        }

        public void UpdatePinArrayAim()
        {
            if (controlFSM == null) return;

            Vector3 currentPosition = transform.position;
            Vector3 playerPosition = playerTransform != null ? playerTransform.position : currentPosition;
            Vector3 attackDirection = (playerPosition - currentPosition);
            if (attackDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            attackDirection.Normalize();
            float attackAngle = Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg - 3f;

            var attackRotationVar = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation");
            if (attackRotationVar != null)
            {
                attackRotationVar.Value = attackAngle;
            }

            var attackAngleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Angle");
            if (attackAngleVar != null)
            {
                attackAngleVar.Value = attackAngle;
            }

            transform.rotation = Quaternion.Euler(0f, 0f, attackAngle + 180f);
        }

        /// <summary>
        /// 新增动态变量
        /// </summary>
        public void AddDynamicVariables()
        {
            if (controlFSM == null) return;
            var floatVars = controlFSM.FsmVariables.FloatVariables.ToList();
            _orbitRadiusVar = new FsmFloat("OrbitRadius") { Value = 7f };
            floatVars.Add(_orbitRadiusVar);
            _orbitSpeedVar = new FsmFloat("OrbitSpeed") { Value = 200f };
            floatVars.Add(_orbitSpeedVar);
            _orbitOffsetVar = new FsmFloat("OrbitOffset") { Value = 0f };
            floatVars.Add(_orbitOffsetVar);
            _dashRotationOffsetVar = new FsmFloat("Dash Rotation Offset") { Value = 45f };
            floatVars.Add(_dashRotationOffsetVar);
            controlFSM.FsmVariables.FloatVariables = floatVars.ToArray();
            _ReadyVar = controlFSM.FsmVariables.FindFsmBool("Ready");
        }
        /// <summary>
        /// 注册Finger Blade FSM的所有事件
        /// </summary>
        private void RegisterFingerBladeEvents()
        {
            if (controlFSM == null) return;

            // 创建或获取事件
            _orbitStartEvent = FsmEvent.GetFsmEvent($"ORBIT START {parentHandName} Blade {bladeIndex}");
            _shootEvent = FsmEvent.GetFsmEvent($"SHOOT {parentHandName} Blade {bladeIndex}");
            _dashOrbitStartEvent = FsmEvent.GetFsmEvent($"DASH ORBIT START {parentHandName} Blade {bladeIndex}");

            var Events = controlFSM.Fsm.Events.ToList();
            Events.Add(_orbitStartEvent);
            Events.Add(_shootEvent);
            Events.Add(_dashOrbitStartEvent);
            controlFSM.Fsm.Events = Events.ToArray();
        }

        /// <summary>
        /// 重新链接Finger Blade FSM事件引用
        /// </summary>
        private void RelinkFingerBladeEventReferences()
        {
            StartCoroutine(MyCoroutine());
        }
        IEnumerator MyCoroutine()
        {
            yield return new WaitForSeconds(0.1f);
            if (controlFSM == null) yield break;
            controlFSM.FsmVariables.Init();
            controlFSM.Fsm.InitStates();
            controlFSM.Fsm.InitData();
        }
        /// <summary>
        /// 添加环绕攻击全局转换 - 新版本：两个独立状态
        /// </summary>
        public void AddOrbitGlobalTransition()
        {
            if (controlFSM == null) return;
            // 创建第一个状态：Orbit Start（接收环绕指令，持续环绕）
            var orbitStartState = CreateOrbitStartState();
            if (orbitStartState == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 创建Orbit Start状态失败");
                return;
            }

            // 创建第二个状态：Orbit Prepare（准备发射，激活antic子物品）
            var orbitPrepareState = CreateOrbitPrepareState();
            if (orbitPrepareState == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 创建Orbit Prepare状态失败");
                return;
            }

            // 创建第三个状态：Orbit Shoot（接收发射指令，设置攻击参数）
            var orbitShootState = CreateOrbitShootState();
            if (orbitShootState == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 创建Orbit Shoot状态失败");
                return;
            }

            // 添加状态动作
            AddOrbitStartActions(orbitStartState);
            AddOrbitPrepareActions(orbitPrepareState);
            AddOrbitShootActions(orbitShootState);
            orbitStartState.Transitions = new FsmTransition[] { CreateTransition(_shootEvent!, orbitPrepareState) };
            SetFinishedTransition(orbitPrepareState, orbitShootState);

            // 设置 Orbit Shoot 的转换：完成后进入 Shoot 状态
            var shootState = controlFSM!.FsmStates.FirstOrDefault(state => state.Name == "Shoot");
            if (shootState != null)
            {
                SetFinishedTransition(orbitShootState, shootState);
            }

            // 创建全局转换（用Orbit事件跳转到Orbit Start状态）
            var globalTransition = CreateTransition(_orbitStartEvent!, orbitStartState);
            var GlobalTransitions = controlFSM.Fsm.GlobalTransitions.ToList();
            GlobalTransitions.Add(globalTransition);
            controlFSM.Fsm.GlobalTransitions = GlobalTransitions.ToArray();
        }

        public void AddDashOrbitGlobalTransition()
        {
            if (controlFSM == null) return;
            if (_dashOrbitStartEvent == null || _shootEvent == null) return;

            var dashOrbitMoveToPoseState = GetOrCreateState(controlFSM, "Dash Orbit MoveToPose");
            var dashOrbitState = GetOrCreateState(controlFSM, "Dash Orbit");

            dashOrbitMoveToPoseState.Actions = new FsmStateAction[]
            {
                new SetBoolValue
                {
                    boolVariable = _dashReadyVar,
                    boolValue = new FsmBool(false),
                    everyFrame = false
                },
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("CalcDashOrbitTargetPos") { Value = "CalcDashOrbitTargetPos" },
                    parameters = new FsmVar[0],
                    everyFrame = false
                },
                new AnimatePositionTo
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    toValue = _dashOrbitTargetPosVar,
                    localSpace = false,
                    time = new FsmFloat(0.6f),
                    delay = new FsmFloat(0f),
                    speed = new FsmFloat { UseVariable = true },
                    easeType = EaseFsmAction.EaseType.linear,
                    reverse = new FsmBool(false),
                    realTime = false,
                    finishEvent = FsmEvent.Finished
                },
                new AnimateRotationTo
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fromValue = new FsmFloat { UseVariable = true }, // 使用当前角度
                    toValue = _dashOrbitTargetRotationVar,
                    worldSpace = false,
                    negativeSpace = false,
                    time = new FsmFloat(0.6f),
                    delay = new FsmFloat(0f),
                    speed = new FsmFloat { UseVariable = true },
                    easeType = EaseFsmAction.EaseType.linear,
                    reverse = new FsmBool(false),
                    realTime = false
                }
            };

            SetFinishedTransition(dashOrbitMoveToPoseState, dashOrbitState);

            var bossObj = GameObject.Find("Silk Boss");

            // 获取 Damager 变量，用于设置 Layer 和激活
            var damagerVar = controlFSM.FsmVariables.GetFsmGameObject("Damager");

            dashOrbitState.Actions = new FsmStateAction[]
            {
                new SetBoolValue
                {
                    boolVariable = _dashReadyVar,
                    boolValue = new FsmBool(true),
                    everyFrame = false
                },
                new SetBoolValue
                {
                    boolVariable = _ReadyVar,
                    boolValue = new FsmBool(false),
                    everyFrame = false
                },
                new SetLayer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = damagerVar
                    },
                    layer = LayerMask.NameToLayer("Enemy Attack")
                },
                new ActivateGameObjectDelay
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = damagerVar
                    },
                    activate = new FsmBool(true),
                    resetOnExit = true,
                    delay = new FsmFloat(0.4f)
                },
                new OrbitAroundTargetAction
                {
                    targetGameObject = new FsmGameObject { Value = bossObj },
                    orbitRadius = _orbitRadiusVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitRadius"),
                    orbitSpeed = _orbitSpeedVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitSpeed"),
                    orbitAngleOffset = _orbitOffsetVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitOffset"),
                    followTarget = true,
                    positionLerpSpeed = 30f,
                    rotateToFaceCenter = true,
                    pointOutward = false,
                    rotationOffset = 45f,
                    applyDirectionalRotationOffset = true,
                    directionalRotationOffset = 8f,
                    rotationLerpSpeed = 30f,
                    zeroVelocityOnEnter = true,
                    zeroVelocityOnExit = true
                }
            };

            // Dash Orbit 完成后跳转到 Antic Pull 状态
            var anticPullState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Antic Pull");
            if (anticPullState != null)
            {
                dashOrbitState.Transitions = new FsmTransition[]
                {
                    CreateTransition(_shootEvent, anticPullState)
                };
            }

            var GlobalTransitions = controlFSM.Fsm.GlobalTransitions.ToList();
            GlobalTransitions.Add(CreateTransition(_dashOrbitStartEvent, dashOrbitMoveToPoseState));
            controlFSM.Fsm.GlobalTransitions = GlobalTransitions.ToArray();
        }

        private void EnsureDashOrbitVariables()
        {
            if (controlFSM == null) return;

            var vars = controlFSM.FsmVariables;

            var boolVars = vars.BoolVariables.ToList();
            _dashReadyVar = boolVars.FirstOrDefault(v => v.Name == "Dash Ready");
            if (_dashReadyVar == null)
            {
                _dashReadyVar = new FsmBool("Dash Ready") { Value = false };
                boolVars.Add(_dashReadyVar);
                vars.BoolVariables = boolVars.ToArray();
                vars.Init();
            }

            var vec3Vars = vars.Vector3Variables.ToList();
            _dashOrbitTargetPosVar = vec3Vars.FirstOrDefault(v => v.Name == "DashOrbit Target Pos");
            if (_dashOrbitTargetPosVar == null)
            {
                _dashOrbitTargetPosVar = new FsmVector3("DashOrbit Target Pos") { Value = Vector3.zero };
                vec3Vars.Add(_dashOrbitTargetPosVar);
                vars.Vector3Variables = vec3Vars.ToArray();
                vars.Init();
            }

            var floatVars = vars.FloatVariables.ToList();
            _dashOrbitTargetRotationVar = floatVars.FirstOrDefault(v => v.Name == "DashOrbit Target Rotation");
            if (_dashOrbitTargetRotationVar == null)
            {
                _dashOrbitTargetRotationVar = new FsmFloat("DashOrbit Target Rotation") { Value = 0f };
                floatVars.Add(_dashOrbitTargetRotationVar);
                vars.FloatVariables = floatVars.ToArray();
                vars.Init();
            }
        }

        public void CalcDashOrbitTargetPos()
        {
            if (controlFSM == null) return;
            if (_dashOrbitTargetPosVar == null || _dashOrbitTargetRotationVar == null) return;

            var boss = GameObject.Find("Silk Boss");
            Vector3 center = boss != null ? boss.transform.position : transform.position;
            
            // 修正中心值：X 使用 Boss 的 AttackControlFsm 的变量 "Antic X" 的值（如果存在），Y 使用 140
            if (boss != null)
            {
                var attackControlFsm = FSMUtility.LocateMyFSM(boss, "Attack Control");
                if (attackControlFsm != null)
                {
                    var anticXVar = attackControlFsm.FsmVariables.GetFsmFloat("Antic X");
                    if (anticXVar != null)
                    {
                        center.x = anticXVar.Value;
                    }
                }
                center.y = 140f;
            }

            float radius = (_orbitRadiusVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitRadius"))?.Value ?? 5f;
            float angleDeg = (_orbitOffsetVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitOffset"))?.Value ?? 0f;
            float rad = angleDeg * Mathf.Deg2Rad;

            // 计算目标位置
            _dashOrbitTargetPosVar.Value = new Vector3(
                center.x + Mathf.Cos(rad) * radius,
                center.y + Mathf.Sin(rad) * radius,
                center.z
            );

            // 计算目标角度（从 BOSS 中心指向外部，即从中心指向 Finger Blade）
            // 角度应该是从中心到目标位置的方向角度
            _dashOrbitTargetRotationVar.Value = angleDeg + 180f; // 加180度是因为要指向外部
        }

        /// <summary>
        /// 创建Orbit Start状态（接收环绕指令，持续环绕）
        /// </summary>
        private FsmState? CreateOrbitStartState()
        {
            if (controlFSM == null) return null;

            // 创建Orbit Start状态
            var orbitStartState = CreateState(controlFSM.Fsm, "Orbit Start", $"Finger Blade {bladeIndex} 环绕开始状态");

            // 添加状态到FSM
            AddStateToFsm(controlFSM, orbitStartState);

            return orbitStartState;
        }

        /// <summary>
        /// 创建Orbit Prepare状态（准备发射，激活antic子物品）
        /// </summary>
        private FsmState? CreateOrbitPrepareState()
        {
            if (controlFSM == null) return null;

            // 创建Orbit Prepare状态
            var orbitPrepareState = CreateState(controlFSM.Fsm, "Orbit Prepare", $"Finger Blade {bladeIndex} 环绕准备状态");

            // 添加状态到FSM
            AddStateToFsm(controlFSM, orbitPrepareState);

            return orbitPrepareState;
        }

        /// <summary>
        /// 创建Orbit Shoot状态（接收发射指令，设置攻击参数）
        /// </summary>
        private FsmState? CreateOrbitShootState()
        {
            if (controlFSM == null) return null;

            // 创建Orbit Shoot状态
            var orbitShootState = CreateState(controlFSM.Fsm, "Orbit Shoot", $"Finger Blade {bladeIndex} 环绕发射状态");

            // 添加状态到FSM
            AddStateToFsm(controlFSM, orbitShootState);

            return orbitShootState;
        }

        /// <summary>
        /// 添加Orbit Start状态的动作
        /// </summary>
        private void AddOrbitStartActions(FsmState orbitStartState)
        {
            // 动作0：设置前景显示（环绕时显示在墙体前方）
            var setRenderOrderAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("UpdateRenderOrder") { Value = "UpdateRenderOrder" },
                parameters = new FsmVar[]
                {
                    new FsmVar(typeof(bool)) { boolValue = true } // true = 前景显示
                },
                everyFrame = false
            };

            // 动作1：开始环绕（持续环绕，直到收到SHOOT事件）
            // 使用变量引用，这样可以在运行时动态获取最新值
            var orbitAction = new OrbitAroundTargetAction
            {
                orbitRadius = _orbitRadiusVar ?? controlFSM!.FsmVariables.GetFsmFloat("OrbitRadius"),
                orbitSpeed = _orbitSpeedVar ?? controlFSM!.FsmVariables.GetFsmFloat("OrbitSpeed"),
                orbitAngleOffset = _orbitOffsetVar ?? controlFSM!.FsmVariables.GetFsmFloat("OrbitOffset"),
                followTarget = true,
                positionLerpSpeed = 30f,
                rotateToFaceCenter = true,
                pointOutward = true,
                rotationOffset = 0f,
                applyDirectionalRotationOffset = true,
                directionalRotationOffset = 8f,
                rotationLerpSpeed = 30f,
                zeroVelocityOnEnter = true,
                zeroVelocityOnExit = true
            };

            // 动作2：等待SHOOT事件（不设置超时，持续环绕）
            var waitForShootAction = new Wait
            {
                time = new FsmFloat(19f), // 设置很长的超时时间
                finishEvent = _shootEvent // 等待SHOOT事件
            };

            orbitStartState.Actions = new FsmStateAction[] { setRenderOrderAction, orbitAction, waitForShootAction };
        }

        /// <summary>
        /// 添加Orbit Prepare状态的动作
        /// </summary>
        private void AddOrbitPrepareActions(FsmState orbitPrepareState)
        {
            // 动作1：立即停止环绕动作
            var stopOrbitAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopOrbitImmediately") { Value = "StopOrbitImmediately" },
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // 动作2：完全清除当前所有速度
            var clearVelocityAction = new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = Vector2.zero, UseVariable = false },
                x = new FsmFloat { Value = 0f, UseVariable = false },
                y = new FsmFloat { Value = 0f, UseVariable = false },
                everyFrame = false
            };

            // 动作3：设置攻击参数（根据当前位置和角度）
            var setAttackParamsAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SetAttackParameters") { Value = "SetAttackParameters" },
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // 动作4：激活silk_boss_finger_antic子物品
            var activateAnticAction = new ActivateGameObject
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = gameObject.transform.Find("silk_boss_finger_antic")?.gameObject }
                },
                activate = new FsmBool(true),
                recursive = new FsmBool(false),
                resetOnExit = true
            };

            // 动作5：等待0.2秒
            var waitAction = new Wait
            {
                time = new FsmFloat(0.2f),
                finishEvent = FsmEvent.Finished
            };

            orbitPrepareState.Actions = new FsmStateAction[] { stopOrbitAction, clearVelocityAction, setAttackParamsAction, activateAnticAction, waitAction };
        }

        /// <summary>
        /// 添加Orbit Shoot状态的动作
        /// </summary>
        private void AddOrbitShootActions(FsmState orbitShootState)
        {
            // 动作1：确保物理不下落与无残余速度
            var zeroGravityAction = new SetGravity2dScale
            {
                gravityScale = new FsmFloat(0f)
            };

            var zeroVelocityAction = new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = Vector2.zero, UseVariable = false },
                x = new FsmFloat { Value = 0f, UseVariable = false },
                y = new FsmFloat { Value = 0f, UseVariable = false },
                everyFrame = false
            };

            // 动作2：预激活伤害/物理碰撞体，防止直接跳到Shoot时未及时生效
            var damagerVar = controlFSM!.FsmVariables.GetFsmGameObject("Damager");
            var physColliderVar = controlFSM.FsmVariables.GetFsmGameObject("Phys Collider");

            var activateDamager = new ActivateGameObjectDelay
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = damagerVar != null ? damagerVar.Value : null }
                },
                activate = new FsmBool(true),
                // recursive = new FsmBool(false),
                delay = new FsmFloat(0.1f),
                resetOnExit = true
            };

            var activatePhysCollider = new ActivateGameObject
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = physColliderVar != null ? physColliderVar.Value : null }
                },
                activate = new FsmBool(true),
                recursive = new FsmBool(false),
                resetOnExit = true
            };

            // 动作3：等待短暂时间让参数生效
            var waitAction = new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            };

            orbitShootState.Actions = new FsmStateAction[] { zeroGravityAction, zeroVelocityAction, activateDamager, activatePhysCollider, waitAction };
        }

        /// <summary>
        /// 设置攻击参数（根据当前位置和角度）
        /// </summary>
        public void SetAttackParameters()
        {
            if (controlFSM == null) return;

            // 获取当前位置和角度
            Vector3 currentPosition = transform.position;
            Vector3 playerPosition = playerTransform != null ? playerTransform.position : Vector3.zero;

            // 计算攻击方向（从当前位置指向玩家）
            Vector3 attackDirection = (playerPosition - currentPosition).normalized;
            float attackAngle = Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg - 3f;

            // 设置Attack Rotation（攻击旋转角度）
            var attackRotationVar = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation");
            if (attackRotationVar != null)
            {
                attackRotationVar.Value = attackAngle;
            }
            else
            {
                Log.Warn("未找到Attack Rotation变量");
            }

            // 设置Attack Angle（直接供Shoot的SetVelocityAsAngle使用）
            var attackAngleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Angle");
            if (attackAngleVar != null)
            {
                attackAngleVar.Value = attackAngle;
            }
            else
            {
                Log.Warn("未找到Attack Angle变量");
            }

            // 设置Attack Y Scale（攻击Y轴缩放）
            var attackYScaleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Y Scale");
            if (attackYScaleVar != null)
            {
                attackYScaleVar.Value = 1f;
            }
            else
            {
                Log.Warn("未找到Attack Y Scale变量");
            }

            // 设置Travel Time Multiplier（影响Return/移动插值节奏）
            var travelTimeMulVar = controlFSM.FsmVariables.GetFsmFloat("Travel Time Multiplier");
            if (travelTimeMulVar != null)
            {
                // 非快速版本，使用原版默认0.02；如有需要 Hand 侧会改为0.015
                travelTimeMulVar.Value = 0.02f;
            }
            else
            {
                Log.Warn("未找到Travel Time Multiplier变量");
            }

            // 设置Wait Min和Wait Max（等待时间）
            var waitMinVar = controlFSM.FsmVariables.GetFsmFloat("Wait Min");
            if (waitMinVar != null)
            {
                waitMinVar.Value = 0f;
            }
            else
            {
                Log.Warn("未找到Wait Min变量");
            }

            var waitMaxVar = controlFSM.FsmVariables.GetFsmFloat("Wait Max");
            if (waitMaxVar != null)
            {
                waitMaxVar.Value = 0f;
            }
            else
            {
                Log.Warn("未找到Wait Max变量");
            }

            // 设置Quick Recover（快速恢复）
            var quickRecoverVar = controlFSM.FsmVariables.GetFsmBool("Quick Recover");
            if (quickRecoverVar != null)
            {
                quickRecoverVar.Value = false;
            }
            else
            {
                Log.Warn("未找到Quick Recover变量");
            }

            // 设置Thunk Time（命中后停顿时长），避免后续状态立刻结束
            var thunkTimeVar = controlFSM.FsmVariables.GetFsmFloat("Thunk Time");
            if (thunkTimeVar != null)
            {
                // 模拟Attack Start Pause的随机区间[0.75, 0.9]
                thunkTimeVar.Value = UnityEngine.Random.Range(0.75f, 0.9f);
            }
            else
            {
                Log.Warn("未找到Thunk Time变量");
            }

            // 设置Ready（原链路在Attack Start Pause里会置为false）
            var readyVar2 = controlFSM.FsmVariables.GetFsmBool("Ready");
            if (readyVar2 != null)
            {
                readyVar2.Value = false;
            }
            else
            {
                Log.Warn("未找到Ready变量");
            }

        }

        /// <summary>
        /// 添加自定义Stomp攻击状态（垂直和斜向）
        /// </summary>
        public void AddCustomStompStates()
        {
            if (controlFSM == null) return;

            // 创建 Stomp 状态（三个方向）
            CreateStompState("CUSTOM_STOMP_CENTER", "Custom Stomp Center Prepare", 90f, 1f, true, false);   // 垂直下砸
            CreateStompState("CUSTOM_STOMP_LEFT", "Custom Stomp Left Prepare", 120f, 1f, false, false);     // 左侧斜向下（更偏向垂直）
            CreateStompState("CUSTOM_STOMP_RIGHT", "Custom Stomp Right Prepare", 60f, 1f, false, false);    // 右侧斜向下（更偏向垂直）
        }

        /// <summary>
        /// 创建 Stomp 状态（通用方法）- 简化版：只设置参数，然后跳转到 Attack Start Pause
        /// </summary>
        private void CreateStompState(string eventName, string paramsStateName, float rotation, float yScale, bool trackX, bool trackY)
        {
            if (controlFSM == null) return;

            var customEvent = FsmEvent.GetFsmEvent(eventName);
            var events = controlFSM.Fsm.Events.ToList();
            if (!events.Contains(customEvent)) { events.Add(customEvent); controlFSM.Fsm.Events = events.ToArray(); }

            // 创建参数设置状态（模仿原版 Set Stomp）
            var setParamsState = CreateState(controlFSM.Fsm, paramsStateName);
            var actions = new List<FsmStateAction>();

            // 设置 Attack Rotation（针尖旋转角度）
            var rotationVar = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation");
            var setRotation = new SetFloatValue { floatVariable = rotationVar, floatValue = new FsmFloat(rotation), everyFrame = false };
            actions.Add(setRotation);

            // 计算 Attack Y（参考原版：Ground Y + Random(11, 12)）
            var groundYVar = controlFSM.FsmVariables.GetFsmFloat("Ground Y");
            var attackYVar = controlFSM.FsmVariables.GetFsmFloat("Attack Y");
            var setGroundY = new SetFloatValue { floatVariable = attackYVar, floatValue = groundYVar, everyFrame = false };
            var addYRandom = new FloatAddRandom { floatVariable = attackYVar, addMin = new FsmFloat(11f), addMax = new FsmFloat(12f) };
            actions.Add(setGroundY);
            actions.Add(addYRandom);

            // 设置 Attack Y Scale
            var attackYScaleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Y Scale");
            var setYScale = new SetFloatValue { floatVariable = attackYScaleVar, floatValue = new FsmFloat(yScale), everyFrame = false };
            actions.Add(setYScale);

            // 设置 Wait Min/Max（参考原版 Set Stomp）
            var waitMinVar = controlFSM.FsmVariables.GetFsmFloat("Wait Min");
            var waitMaxVar = controlFSM.FsmVariables.GetFsmFloat("Wait Max");
            var setWaitMin = new SetFloatValue { floatVariable = waitMinVar, floatValue = new FsmFloat(0f), everyFrame = false };
            var setWaitMax = new SetFloatValue { floatVariable = waitMaxVar, floatValue = new FsmFloat(0.2f), everyFrame = false };
            actions.Add(setWaitMin);
            actions.Add(setWaitMax);

            // 设置 Quick Recover
            var quickRecoverVar = controlFSM.FsmVariables.GetFsmBool("Quick Recover");
            var setQuickRecover = new SetBoolValue { boolVariable = quickRecoverVar, boolValue = new FsmBool(false), everyFrame = false };
            actions.Add(setQuickRecover);

            // 设置 X Target Min/Max（边界限制）
            var xTargetMinVar = controlFSM.FsmVariables.GetFsmFloat("X Target Min");
            var xTargetMaxVar = controlFSM.FsmVariables.GetFsmFloat("X Target Max");
            var setXMin = new SetFloatValue { floatVariable = xTargetMinVar, floatValue = new FsmFloat(20f), everyFrame = false };
            var setXMax = new SetFloatValue { floatVariable = xTargetMaxVar, floatValue = new FsmFloat(57f), everyFrame = false };
            actions.Add(setXMin);
            actions.Add(setXMax);

            setParamsState.Actions = actions.ToArray();

            // 直接跳转到 Attack Start Pause（让原版流程接管）
            var attackStartPauseState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Attack Start Pause");
            if (attackStartPauseState != null)
            {
                SetFinishedTransition(setParamsState, attackStartPauseState);
            }

            // 添加状态到FSM
            AddStateToFsm(controlFSM, setParamsState);

            // 全局转换指向参数设置状态
            var globalTrans = controlFSM.FsmGlobalTransitions.ToList();
            globalTrans.Add(CreateTransition(customEvent, setParamsState));
            controlFSM.Fsm.GlobalTransitions = globalTrans.ToArray();
        }

        /// <summary>
        /// 添加自定义Swipe攻击状态（区分左右方向）
        /// </summary>
        public void AddCustomSwipeStates()
        {
            if (controlFSM == null) return;

            // 创建 Swipe L 和 Swipe R 状态
            // Swipe L: rotation=0°（向右），yScale=-1（贴图翻转以匹配向右攻击）
            // Swipe R: rotation=180°（向左），yScale=1（贴图正常）
            CreateSwipeState("CUSTOM_SWIPE_L", "Custom Swipe L Prepare", 0f, -1f, 23f, 55f); // 从左侧出现，朝向右，向右攻击
            CreateSwipeState("CUSTOM_SWIPE_R", "Custom Swipe R Prepare", 180f, 1f, 23f, 55f); // 从右侧出现，朝向左，向左政击
        }

        /// <summary>
        /// 创建 Swipe 状态（通用方法）- 简化版：只设置参数，然后跳转到 Attack Start Pause
        /// </summary>
        private void CreateSwipeState(string eventName, string paramsStateName, float rotation, float yScale, float xMin, float xMax)
        {
            if (controlFSM == null) return;

            var customEvent = FsmEvent.GetFsmEvent(eventName);
            var events = controlFSM.Fsm.Events.ToList();
            if (!events.Contains(customEvent)) { events.Add(customEvent); controlFSM.Fsm.Events = events.ToArray(); }

            // 创建参数设置状态（模仿原版 Set Swipe L/R）
            var setParamsState = CreateState(controlFSM.Fsm, paramsStateName);
            var actions = new List<FsmStateAction>();

            // 设置 Attack Rotation（针尖旋转角度）
            var rotationVar = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation");
            var setRotation = new SetFloatValue { floatVariable = rotationVar, floatValue = new FsmFloat(rotation), everyFrame = false };
            actions.Add(setRotation);

            // 设置 Attack Y Scale
            var attackYScaleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Y Scale");
            var setYScale = new SetFloatValue { floatVariable = attackYScaleVar, floatValue = new FsmFloat(yScale), everyFrame = false };
            actions.Add(setYScale);

            // 设置 Wait Min/Max（参考原版 Set Swipe L/R）
            var waitMinVar = controlFSM.FsmVariables.GetFsmFloat("Wait Min");
            var waitMaxVar = controlFSM.FsmVariables.GetFsmFloat("Wait Max");
            var setWaitMin = new SetFloatValue { floatVariable = waitMinVar, floatValue = new FsmFloat(0f), everyFrame = false };
            var setWaitMax = new SetFloatValue { floatVariable = waitMaxVar, floatValue = new FsmFloat(0.2f), everyFrame = false };
            actions.Add(setWaitMin);
            actions.Add(setWaitMax);

            // 设置 Quick Recover
            var quickRecoverVar = controlFSM.FsmVariables.GetFsmBool("Quick Recover");
            var setQuickRecover = new SetBoolValue { boolVariable = quickRecoverVar, boolValue = new FsmBool(false), everyFrame = false };
            actions.Add(setQuickRecover);

            // 设置 X Target Min/Max（边界限制）
            var xTargetMinVar = controlFSM.FsmVariables.GetFsmFloat("X Target Min");
            var xTargetMaxVar = controlFSM.FsmVariables.GetFsmFloat("X Target Max");
            var setXMin = new SetFloatValue { floatVariable = xTargetMinVar, floatValue = new FsmFloat(xMin), everyFrame = false };
            var setXMax = new SetFloatValue { floatVariable = xTargetMaxVar, floatValue = new FsmFloat(xMax), everyFrame = false };
            actions.Add(setXMin);
            actions.Add(setXMax);

            setParamsState.Actions = actions.ToArray();

            // 直接跳转到 Attack Start Pause（让原版流程接管）
            var attackStartPauseState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Attack Start Pause");
            if (attackStartPauseState != null)
            {
                SetFinishedTransition(setParamsState, attackStartPauseState);
            }

            // 添加状态到FSM
            AddStateToFsm(controlFSM, setParamsState);

            // 全局转换指向参数设置状态
            var globalTrans = controlFSM.FsmGlobalTransitions.ToList();
            globalTrans.Add(CreateTransition(customEvent, setParamsState));
            controlFSM.Fsm.GlobalTransitions = globalTrans.ToArray();
        }

        /// <summary>
        /// 立即停止环绕（供FSM调用）
        /// </summary>
        public void StopOrbitImmediately()
        {
            // 立即停止移动，确保没有惯性
            if (gameObject != null)
            {
                // 获取Rigidbody2D组件并立即停止移动
                var rb2d = gameObject.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    rb2d.linearVelocity = Vector2.zero;
                    rb2d.angularVelocity = 0f;
                }
            }
        }

        /// <summary>
        /// 检查是否为二阶段 Swipe 攻击，并发送相应事件（供FSM调用）
        /// </summary>
        public void CheckAndSendPhase2SwipeEvent()
        {
            if (controlFSM == null) return;

            // 获取变量
            var rotationVar = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation");
            var specialAttackVar = controlFSM.FsmVariables.GetFsmBool("Special Attack");

            if (rotationVar == null || specialAttackVar == null) return;

            float rotation = rotationVar.Value;
            bool isSpecialAttack = specialAttackVar.Value;

            // 检查是否为 Swipe 攻击（Rotation 接近 0 或 180）
            bool isSwipe = (Mathf.Abs(rotation) < 2f) || (Mathf.Abs(rotation - 180f) < 2f);

            // 如果是 Swipe 且 Special Attack 为 true，发送事件
            if (isSwipe && isSpecialAttack)
            {
                controlFSM.SendEvent("IS_PHASE2_SWIPE");
            }
        }

        /// <summary>
        /// 修改 Antic Pull 状态：调整等待时间
        /// </summary>
        private void ModifyAnticPullState()
        {
            if (controlFSM == null) return;

            var anticPullState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Antic Pull");
            if (anticPullState == null)
            {
                Log.Warn($"Finger Blade {bladeIndex} 未找到 Antic Pull 状态");
                return;
            }

            var actions = anticPullState.Actions.ToList();
            int waitCount = 0;
            int waitBoolCount = 0;

            // 遍历所有动作，修改 Wait 和 WaitBool 的时间
            for (int i = 0; i < actions.Count; i++)
            {
                // 修改第一个 Wait 的 time 为 0.48
                if (actions[i] is Wait wait)
                {
                    waitCount++;
                    if (waitCount == 1)
                    {
                        wait.time = new FsmFloat(0.48f);
                    }
                }
                // 修改第一个 WaitBool 的 time 为 0.36
                else if (actions[i] is WaitBool waitBool)
                {
                    waitBoolCount++;
                    if (waitBoolCount == 1)
                    {
                        waitBool.time = new FsmFloat(0.36f);
                    }
                }
            }
            actions.Insert(0, new WaitBool
            {
                boolTest = controlFSM.FsmVariables.GetFsmBool("Special Attack"),
                time = new FsmFloat(0.25f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });
            anticPullState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 修改 Antic Pause 状态：添加二阶段Swipe追踪判断
        /// </summary>
        private void ModifyAnticPauseState()
        {
            if (controlFSM == null) return;

            var anticPauseState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Antic Pause");
            if (anticPauseState == null)
            {
                Log.Warn($"Finger Blade {bladeIndex} 未找到 Antic Pause 状态");
                return;
            }

            // 添加 Special Attack 和 Track Time 变量，并缓存引用
            var boolVars = controlFSM.FsmVariables.BoolVariables.ToList();
            _specialAttackVar = boolVars.FirstOrDefault(v => v.Name == "Special Attack");
            if (_specialAttackVar == null)
            {
                _specialAttackVar = new FsmBool("Special Attack") { Value = false };
                boolVars.Add(_specialAttackVar);
                controlFSM.FsmVariables.BoolVariables = boolVars.ToArray();
            }

            var floatVars = controlFSM.FsmVariables.FloatVariables.ToList();
            _trackTimeVar = floatVars.FirstOrDefault(v => v.Name == "Track Time");
            if (_trackTimeVar == null)
            {
                _trackTimeVar = new FsmFloat("Track Time") { Value = 1f };
                floatVars.Add(_trackTimeVar);
                controlFSM.FsmVariables.FloatVariables = floatVars.ToArray();
            }

            // 创建追踪状态
            CreatePhase2TrackState();

        }

        /// <summary>
        /// 创建二阶段追踪状态
        /// </summary>
        private void CreatePhase2TrackState()
        {
            if (controlFSM == null) return;

            var trackState = CreateState(controlFSM.Fsm, "Phase2 Track");
            var actions = new List<FsmStateAction>();

            // 追踪玩家
            var trackAction = new SoftLockTrackAction
            {
                followX = false,
                followY = true,
                followZ = false,
                maintainInitialOffset = true,
                positionLerpSpeed = 3f,
                useRigidbody = true,
                useMinY = true,
                minY = 132f,
                rotationZ = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation"),
                rotationLerpSpeed = 20f,
                zeroVelocityOnEnter = true,
                zeroVelocityOnExit = true
            };
            actions.Add(trackAction);

            // 等待追踪时间
            var waitTrack = new Wait
            {
                time = _trackTimeVar,
                finishEvent = FsmEvent.Finished
            };
            actions.Add(waitTrack);

            trackState.Actions = actions.ToArray();

            // 添加转换：Track -> Antic Pull
            var anticPullState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Antic Pull");
            if (anticPullState != null)
            {
                trackState.Transitions = new FsmTransition[]
                {
                    new FsmTransition { FsmEvent = FsmEvent.Finished, toState = "Antic Pull", toFsmState = anticPullState }
                };
            }

            // 添加状态到FSM
            var states = controlFSM.Fsm.States.ToList();
            states.Add(trackState);
            controlFSM.Fsm.States = states.ToArray();

            // 初始化所有 Actions（关键步骤！）
            foreach (var action in trackState.Actions)
            {
                action.Init(trackState);
            }
            // 修改 Antic Pause 的转换逻辑
            ModifyAnticPauseTransitions(trackState);
        }

        /// <summary>
        /// 修改 Antic Pause 的转换：添加二阶段Swipe判断
        /// </summary>
        private void ModifyAnticPauseTransitions(FsmState trackState)
        {
            if (controlFSM == null) return;

            var anticPauseState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Antic Pause");
            if (anticPauseState == null) return;

            // 在 Antic Pause 末尾添加判断逻辑
            var actions = anticPauseState.Actions.ToList();

            // 获取/创建 Bool 变量
            var rotationVar = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation");
            var specialAttackVar = _specialAttackVar;

            // 创建 "Is Swipe" Bool 变量用于存储 Float 比较结果
            var boolVars = controlFSM.FsmVariables.BoolVariables.ToList();
            var isSwipeVar = boolVars.FirstOrDefault(v => v.Name == "Is Swipe");
            if (isSwipeVar == null)
            {
                isSwipeVar = new FsmBool("Is Swipe") { Value = false };
                boolVars.Add(isSwipeVar);
                controlFSM.FsmVariables.BoolVariables = boolVars.ToArray();
            }

            // 注册事件
            var events = controlFSM.Fsm.Events.ToList();
            var isPhase2SwipeEvent = FsmEvent.GetFsmEvent("IS_PHASE2_SWIPE");
            if (!events.Contains(isPhase2SwipeEvent))
            {
                events.Add(isPhase2SwipeEvent);
            }
            controlFSM.Fsm.Events = events.ToArray();

            // 使用 CallMethod 调用自定义判断方法
            var checkPhase2Swipe = new CallMethod
            {
                behaviour = this,
                methodName = "CheckAndSendPhase2SwipeEvent",
                parameters = new FsmVar[0],
                everyFrame = false
            };
            actions.Add(checkPhase2Swipe);

            anticPauseState.Actions = actions.ToArray();

            // 修改转换：只有 IS_PHASE2_SWIPE 事件才跳转到 Phase2 Track
            var transitions = anticPauseState.Transitions.ToList();
            transitions.Add(new FsmTransition
            {
                FsmEvent = isPhase2SwipeEvent,
                toState = "Phase2 Track",
                toFsmState = trackState
            });
            anticPauseState.Transitions = transitions.ToArray();
        }
        private void ModifyShootState()
        {
            if (controlFSM == null) return;

            // 查找 Shoot 状态
            var shootState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Shoot");
            if (shootState == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 Shoot 状态");
                return;
            }

            // 创建 CallMethod 动作
            var callMethodAction = new CallMethod
            {
                behaviour = this,
                methodName = "SpawnAndFirePin",
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // 在 Actions 数组开头插入
            var actions = shootState.Actions.ToList();
            actions.Insert(0, callMethodAction);
            shootState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 修改 Thunk 状态：将 Damager 的 layer 重置回 Attack 层级，并恢复默认显示层级
        /// </summary>
        private void ModifyThunkState()
        {
            if (controlFSM == null) return;

            // 查找 Thunk 状态
            var thunkState = controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Thunk");
            if (thunkState == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 Thunk 状态");
                return;
            }

            // 获取 Damager 变量
            var damagerVar = controlFSM.FsmVariables.GetFsmGameObject("Damager");
            if (damagerVar == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 Damager 变量");
                return;
            }

            // 创建 SetLayer 动作，将 Damager 重置回 Attack 层级
            var setLayerAction = new SetLayer
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = damagerVar
                },
                layer = LayerMask.NameToLayer("Attack")
            };

            // 创建恢复显示层级的动作
            var restoreRenderOrderAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("UpdateRenderOrder") { Value = "UpdateRenderOrder" },
                parameters = new FsmVar[]
                {
                    new FsmVar(typeof(bool)) { boolValue = false } // false = 恢复默认
                },
                everyFrame = false
            };

            // 在 Actions 数组开头插入（确保在状态进入时立即执行）
            var actions = thunkState.Actions.ToList();
            actions.Insert(0, restoreRenderOrderAction);
            actions.Insert(0, setLayerAction);
            thunkState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 生成并发射 Pin Projectile
        /// 在 Shoot 状态开头被 FSM 调用
        /// </summary>
        public void SpawnAndFirePin()
        {
            if (_pinManager == null || !_pinManager.IsInitialized)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] FWPinManager 未就绪，跳过 Pin 发射");
                return;
            }

            if (controlFSM == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] Control FSM 为空，无法发射 Pin");
                return;
            }

            // 1. 获取当前位置
            Vector3 pinPosition = transform.position;

            // 2. 获取攻击角度（从 FSM 变量 Attack Angle 获取）
            float attackAngle = 0f;
            var attackAngleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Angle");
            if (attackAngleVar != null)
            {
                attackAngle = attackAngleVar.Value;
            }
            else
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 Attack Angle 变量，使用默认角度 0");
            }

            // 3. 从池子获取 Pin
            var pin = _pinManager.SpawnPinProjectile(pinPosition);
            if (pin == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 无法获取 Pin Projectile");
                return;
            }

            // 4. 设置 Pin 的旋转角度（Fire 状态会用 GetRotation 获取这个角度）
            pin.transform.rotation = Quaternion.Euler(0f, 0f, attackAngle);

            // 5. 发送 DIRECT_FIRE 事件触发 Pin 进入 Antic 状态
            var pinFsm = pin.LocateMyFSM("Control");
            if (pinFsm != null)
            {
                pinFsm.SendEvent("DIRECT_FIRE");
                // 进入 Antic 后等待 0.5s 再触发 ATTACK 事件，让 FSM 继续攻击流程
                StartCoroutine(DelayedAttack(pinFsm));
            }
            else
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] Pin 未找到 Control FSM");
            }
        }

        /// <summary>
        /// 延迟发送 ATTACK 事件，驱动 Pin FSM 从 Antic 进入后续攻击
        /// </summary>
        private IEnumerator DelayedAttack(PlayMakerFSM fsm)
        {
            yield return new WaitForSeconds(0.5f);
            if (fsm != null)
            {
                fsm.SendEvent("ATTACK");
            }
        }

        /// <summary>
        /// 更新 Finger Blade 的显示层级（参照 SilkBallBehavior.UpdateRenderOrder）
        /// </summary>
        /// <param name="toFront">true=显示在前景(sortingOrder=10)，false=恢复默认(sortingOrder=0)</param>
        public void UpdateRenderOrder(bool toFront)
        {
            if (_meshRenderer == null) return;

            if (toFront)
            {
                // 环绕/穿墙：设置较高的 sortingOrder 显示在墙体前面
                _meshRenderer.sortingOrder = 10;
            }
            else
            {
                // 普通状态：恢复默认 sortingOrder
                _meshRenderer.sortingOrder = 0;
            }
        }
    }
}