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
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    /// <summary>
    /// Finger Blade控制Behavior - 管理单根Finger Blade的行为
    /// </summary>
    internal class FingerBladeBehavior : MonoBehaviour
    {
        [Header("Finger Blade配置")]
        public int bladeIndex = -1;              // 在手中的索引
        public string parentHandName = "";       // 父手部名称
        public HandControlBehavior? parentHand; // 父手部Behavior引用

        [Header("环绕参数")]
        public float orbitRadius = 7f;             // 环绕半径（增大到6f）
        public float orbitSpeed = 200f;           // 环绕速度（稍微减慢）
        public float orbitOffset = 0f;            // 环绕角度偏移

        // 私有变量
        private PlayMakerFSM? controlFSM;        // Control FSM
        private PlayMakerFSM? tinkFSM;           // Tink FSM
        private Transform? playerTransform;      // 玩家Transform
                                                 // 运行时缓存：新增的FSM变量引用，确保动作字段绑定到同一实例
        private FsmFloat? _orbitRadiusVar;
        private FsmFloat? _orbitSpeedVar;
        private FsmFloat? _orbitOffsetVar;
        private FsmFloat? _trackTimeVar;         // Track Time 变量缓存
        private FsmBool? _specialAttackVar;      // Special Attack 变量缓存

        // 事件引用缓存
        private FsmEvent? _orbitStartEvent;
        private FsmEvent? _shootEvent;

        /// <summary>
        /// 初始化Finger Blade
        /// </summary>
        public void Initialize(int index, string handName, HandControlBehavior hand)
        {
            // 修改bladeIndex为全局唯一：Hand L(0-2), Hand R(3-5)
            int globalIndex = handName == "Hand L" ? index : index + 3;
            bladeIndex = globalIndex;
            parentHandName = handName;
            parentHand = hand;

            // Log.Info($"初始化Finger Blade {bladeIndex} (局部索引{index}) ({gameObject.name}) 在 {parentHandName} 下");
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

            if (controlFSM == null)
            {
                Log.Warn($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到Control FSM");
            }
            AddDynamicVariables();
            AddCustomStompStates();
            AddCustomSwipeStates();
            ModifyAnticPullState();  // 修改 Antic Pull 状态的等待时间
            ModifyAnticPauseState(); // 修改 Antic Pause 状态，添加二阶段追踪判断

            // 统一初始化FSM（在所有状态和事件添加完成后）
            RelinkFingerBladeEventReferences();
        }
        /// <summary>
        /// 新增动态变量
        /// </summary>
        public void AddDynamicVariables()
        {
            if (controlFSM == null) return;

            // 复用已存在的变量，否则创建并添加
            var floatVars = controlFSM.FsmVariables.FloatVariables.ToList();
            _orbitRadiusVar = floatVars.FirstOrDefault(v => v.Name == "OrbitRadius");
            _orbitSpeedVar = floatVars.FirstOrDefault(v => v.Name == "OrbitSpeed");
            _orbitOffsetVar = floatVars.FirstOrDefault(v => v.Name == "OrbitOffset");

            bool added = false;
            if (_orbitRadiusVar == null)
            {
                _orbitRadiusVar = new FsmFloat("OrbitRadius") { Value = orbitRadius };
                floatVars.Add(_orbitRadiusVar);
                added = true;
            }
            else
            {
                _orbitRadiusVar.Value = orbitRadius;
            }

            if (_orbitSpeedVar == null)
            {
                _orbitSpeedVar = new FsmFloat("OrbitSpeed") { Value = orbitSpeed };
                floatVars.Add(_orbitSpeedVar);
                added = true;
            }
            else
            {
                _orbitSpeedVar.Value = orbitSpeed;
            }

            if (_orbitOffsetVar == null)
            {
                _orbitOffsetVar = new FsmFloat("OrbitOffset") { Value = orbitOffset };
                floatVars.Add(_orbitOffsetVar);
                added = true;
            }
            else
            {
                _orbitOffsetVar.Value = orbitOffset;
            }

            if (added)
            {
                controlFSM.FsmVariables.FloatVariables = floatVars.ToArray();
            }
        }


        /// <summary>
        /// 设置环绕参数（已废弃，现在由Hand通过SetFsmFloat直接设置FSM变量）
        /// </summary>
        [System.Obsolete("此方法已废弃，请使用Hand中的SetFsmFloat直接设置FSM变量")]
        public void SetOrbitParameters(float radius, float speed, float offset)
        {
            // 此方法保留仅用于兼容性，实际参数应由Hand通过SetFsmFloat设置
            Log.Warn($"Finger Blade {bladeIndex} SetOrbitParameters 已废弃，请使用SetFsmFloat");
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

            // 将事件添加到FSM的事件列表中
            var existingEvents = controlFSM.FsmEvents.ToList();

            if (!existingEvents.Contains(_orbitStartEvent))
            {
                existingEvents.Add(_orbitStartEvent);
            }
            if (!existingEvents.Contains(_shootEvent))
            {
                existingEvents.Add(_shootEvent);
            }

            // 使用反射设置FsmEvents
            var fsmType = controlFSM.Fsm.GetType();
            var eventsField = fsmType.GetField("events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (eventsField != null)
            {
                eventsField.SetValue(controlFSM.Fsm, existingEvents.ToArray());
            }
            else
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到events字段");
            }
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

            // 首先注册所有需要的事件
            RegisterFingerBladeEvents();

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

            // 添加状态转换
            if (_shootEvent != null)
            {
                orbitStartState.Transitions = new FsmTransition[] { CreateTransition(_shootEvent, orbitPrepareState) };
            }
            SetFinishedTransition(orbitPrepareState, orbitShootState);
            var shootState = controlFSM!.FsmStates.FirstOrDefault(state => state.Name == "Shoot");
            if (shootState != null)
            {
                SetFinishedTransition(orbitShootState, shootState);
            }

            // 创建全局转换（从Idle到Orbit Start）
            var globalTransition = CreateTransition(_orbitStartEvent!, orbitStartState);

            // 添加全局转换
            var existingTransitions = controlFSM.fsm.globalTransitions.ToList();
            existingTransitions.Add(globalTransition);
            controlFSM.fsm.globalTransitions = existingTransitions.ToArray();

            // 重新链接所有事件引用
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

            orbitStartState.Actions = new FsmStateAction[] { orbitAction, waitForShootAction };
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
                x = new FsmFloat(0f),
                y = new FsmFloat(0f)
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
                x = new FsmFloat(0f),
                y = new FsmFloat(0f)
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
                setParamsState.Transitions = new FsmTransition[]
                {
                    new FsmTransition { FsmEvent = FsmEvent.Finished, toState = "Attack Start Pause", toFsmState = attackStartPauseState }
                };
            }

            // 添加状态到FSM
            var states = controlFSM.FsmStates.ToList();
            states.Add(setParamsState);
            controlFSM.Fsm.States = states.ToArray();

            // 全局转换指向参数设置状态
            var globalTrans = controlFSM.FsmGlobalTransitions.ToList();
            globalTrans.Add(new FsmTransition
            {
                FsmEvent = customEvent,
                toState = paramsStateName,
                toFsmState = setParamsState
            });
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

            // Log.Info($"Finger Blade {bladeIndex} Antic Pause 状态已修改");
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

            // Log.Info($"Finger Blade {bladeIndex} Phase2 Track 状态已创建并初始化");

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
    }
}