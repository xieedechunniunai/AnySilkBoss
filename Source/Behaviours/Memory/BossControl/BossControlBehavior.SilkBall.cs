using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityObject = UnityEngine.Object;
using HazardType = GlobalEnums.HazardType;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;
namespace AnySilkBoss.Source.Behaviours.Memory
{
    internal partial class MemoryBossBehavior : MonoBehaviour
    {
        #region 移动丝球攻击状态创建

        /// <summary>
        /// 修改BossControl FSM，添加移动丝球攻击状态
        /// </summary>
        private void ModifyBossControlFSM()
        {
            if (_bossControlFsm == null)
            {
                Log.Error("BossControl FSM未初始化，无法修改");
                return;
            }

            Log.Info("修改Boss Control FSM");

            // 创建移动丝球攻击状态链
            CreateSilkBallDashStates();
            // ModifyOriginFsm();

            // 添加大丝球大招锁定状态
            CreateBigSilkBallLockState();

            // 添加爬升阶段漫游状态
            CreateClimbRoamStates();

            // 添加全局事件监听
            AddGlobalEventListeners();

            // 添加眩晕时的丝球清理逻辑
            AddStunInterruptHandling();

            // 添加爬升阶段修复
            InitializeClimbCastProtection();

            // 添加 BlastBurst3 协作状态
            CreateBlastBurst3BridgeStates();

            SetupControlIdlePendingTransitions();

            ReinitializeFsmVariables(_bossControlFsm);
            Log.Info("Boss Control FSM修改完成");
        }

        /// <summary>
        /// 创建移动丝球攻击的状态链
        /// </summary>
        private void CreateSilkBallDashStates()
        {
            if (_bossControlFsm == null) return;

            Log.Info("=== 开始创建移动丝球攻击状态链 ===");


            // 2. 创建隐形目标点GameObject
            CreateInvisibleTargetPoints();

            // 3. 初始化Route Point变量（X和Y分开）
            RoutePoint0X = new FsmFloat("Route Point 0 X") { Value = 0f };
            RoutePoint0Y = new FsmFloat("Route Point 0 Y") { Value = 0f };
            RoutePoint1X = new FsmFloat("Route Point 1 X") { Value = 0f };
            RoutePoint1Y = new FsmFloat("Route Point 1 Y") { Value = 0f };
            RoutePoint2X = new FsmFloat("Route Point 2 X") { Value = 0f };
            RoutePoint2Y = new FsmFloat("Route Point 2 Y") { Value = 0f };

            // ⚠️ Phase2 Special点位
            RoutePointSpecialX = new FsmFloat("Route Point Special X") { Value = 0f };
            RoutePointSpecialY = new FsmFloat("Route Point Special Y") { Value = 0f };

            // 4. 初始化时间变量
            IdleWaitTime = new FsmFloat("Idle Wait Time") { Value = IDLE_WAIT_TIME };

            // 5. 初始化FsmGameObject变量（指向隐形目标点）
            _fsmTargetPoint0 = new FsmGameObject("Target Point 0") { Value = _targetPoint0 };
            _fsmTargetPoint1 = new FsmGameObject("Target Point 1") { Value = _targetPoint1 };
            _fsmTargetPoint2 = new FsmGameObject("Target Point 2") { Value = _targetPoint2 };
            _fsmTargetPointSpecial = new FsmGameObject("Target Point Special") { Value = _targetPointSpecial };

            // 5. 添加到FSM变量列表
            var floatVars = _bossControlFsm.FsmVariables.FloatVariables.ToList();
            floatVars.Add(RoutePoint0X);
            floatVars.Add(RoutePoint0Y);
            floatVars.Add(RoutePoint1X);
            floatVars.Add(RoutePoint1Y);
            floatVars.Add(RoutePoint2X);
            floatVars.Add(RoutePoint2Y);
            floatVars.Add(RoutePointSpecialX);
            floatVars.Add(RoutePointSpecialY);
            floatVars.Add(IdleWaitTime);
            _bossControlFsm.FsmVariables.FloatVariables = floatVars.ToArray();

            var gameObjectVars = _bossControlFsm.FsmVariables.GameObjectVariables.ToList();
            gameObjectVars.Add(_fsmTargetPoint0);
            gameObjectVars.Add(_fsmTargetPoint1);
            gameObjectVars.Add(_fsmTargetPoint2);
            gameObjectVars.Add(_fsmTargetPointSpecial);
            _bossControlFsm.FsmVariables.GameObjectVariables = gameObjectVars.ToArray();

            // ⚠️ 创建Special Attack变量（Phase2特殊攻击标志）
            EnsureBoolVariable(_bossControlFsm, "Special Attack");

            Log.Info("Route Point变量（包括Special）、时间变量、目标点GameObject变量和Special Attack变量已创建");

            // 创建所有状态（包括Special路径）
            var dashAnticSpecialState = CreateDashAnticState(-1);  // -1表示Special
            var dashToSpecialState = CreateDashToPointState(-1);
            var idleAtSpecialState = CreateIdleAtPointState(-1);

            var dashAntic0State = CreateDashAnticState(0);
            var dashToPoint0State = CreateDashToPointState(0);
            var idleAtPoint0State = CreateIdleAtPointState(0);

            var dashAntic1State = CreateDashAnticState(1);
            var dashToPoint1State = CreateDashToPointState(1);
            var idleAtPoint1State = CreateIdleAtPointState(1);

            var dashAntic2State = CreateDashAnticState(2);
            var dashToPoint2State = CreateDashToPointState(2);
            var dashEndState = CreateSilkBallDashEndState();

            // 使用 FsmStateBuilder 批量添加状态
            AddStatesToFsm(_bossControlFsm,
                dashAnticSpecialState, dashToSpecialState, idleAtSpecialState,
                dashAntic0State, dashToPoint0State, idleAtPoint0State,
                dashAntic1State, dashToPoint1State, idleAtPoint1State,
                dashAntic2State, dashToPoint2State, dashEndState);

            // 设置转换（包括Special路径）
            SetupSilkBallDashTransitions(
                dashAnticSpecialState, dashToSpecialState, idleAtSpecialState,
                dashAntic0State, dashToPoint0State, idleAtPoint0State,
                dashAntic1State, dashToPoint1State, idleAtPoint1State,
                dashAntic2State, dashToPoint2State,
                dashEndState);

            // 初始化FSM
            // ReinitializeFsm(_bossControlFsm);

            Log.Info("移动丝球状态链创建完成");
        }

        /// <summary>
        /// 设置移动丝球状态链的转换关系（包括Special路径）
        /// </summary>
        private void SetupSilkBallDashTransitions(
            FsmState dashAnticSpecial, FsmState dashToSpecial, FsmState idleAtSpecial,
            FsmState dashAntic0, FsmState dashToPoint0, FsmState idleAtPoint0,
            FsmState dashAntic1, FsmState dashToPoint1, FsmState idleAtPoint1,
            FsmState dashAntic2, FsmState dashToPoint2,
            FsmState dashEnd)
        {
            // 使用 SetFinishedTransition 简化所有 FINISHED 转换

            // Special路径（Phase2才使用）
            SetFinishedTransition(dashAnticSpecial, dashToSpecial);
            SetFinishedTransition(dashToSpecial, idleAtSpecial);
            SetFinishedTransition(idleAtSpecial, dashAntic0);

            // Point 0
            SetFinishedTransition(dashAntic0, dashToPoint0);
            SetFinishedTransition(dashToPoint0, idleAtPoint0);
            SetFinishedTransition(idleAtPoint0, dashAntic1);

            // Point 1
            SetFinishedTransition(dashAntic1, dashToPoint1);
            SetFinishedTransition(dashToPoint1, idleAtPoint1);
            SetFinishedTransition(idleAtPoint1, dashAntic2);

            // Point 2
            SetFinishedTransition(dashAntic2, dashToPoint2);
            SetFinishedTransition(dashToPoint2, dashEnd);

            // Dash End -> Idle
            var idleState = FindState(_bossControlFsm!, "Idle");
            if (idleState != null)
            {
                SetFinishedTransition(dashEnd, idleState);
            }

            Log.Info("移动丝球状态链转换设置完成（包括Special路径）");
        }

        /// <summary>
        /// 创建Dash准备状态
        /// </summary>
        private FsmState CreateDashAnticState(int pointIndex)
        {
            string stateName = pointIndex == -1 ? "Dash Antic Special" : $"Dash Antic {pointIndex}";
            string description = pointIndex == -1 ? "准备冲刺到Special点位" : $"准备冲刺到点位 {pointIndex}";

            var state = CreateState(_bossControlFsm!.Fsm, stateName, description);

            var actions = new List<FsmStateAction>();

            // 获取目标GameObject和坐标
            FsmGameObject? targetObject = null;

            switch (pointIndex)
            {
                case -1:  // Special点位
                    targetObject = _fsmTargetPointSpecial;
                    break;
                case 0:
                    targetObject = _fsmTargetPoint0;
                    break;
                case 1:
                    targetObject = _fsmTargetPoint1;
                    break;
                case 2:
                    targetObject = _fsmTargetPoint2;
                    break;
            }

            if (targetObject == null)
            {
                Log.Error($"CreateDashAnticState: Target object for point {pointIndex} is null.");
                return state;
            }
            // 2. 面向目标点
            actions.Add(new FaceObjectV2
            {
                objectA = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                objectB = targetObject,
                spriteFacesRight = false,
                playNewAnimation = false,
                resetFrame = false,
                pauseBetweenTurns = 0,
                everyFrame = false
            });

            // 3. 播放Dash Antic动画
            actions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Dash Antic") { Value = "Dash Antic" },
                animationCompleteEvent = null // 不需要完成事件
            });

            // 4. 等待准备时间（第一次Antic使用更长时间）
            float anticTime = pointIndex == 0 ? DASH_ANTIC_TIME_FIRST : DASH_ANTIC_TIME;
            actions.Add(new Wait
            {
                time = anticTime,
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            if (pointIndex == 0 && _bossControlFsm != null)
            {
                var dashPendingVar = EnsureBoolVariable(_bossControlFsm, "Silk Ball Dash Pending");
                actions.Insert(0, new SetBoolValue
                {
                    boolVariable = dashPendingVar,
                    boolValue = new FsmBool(false)
                });
            }

            state.Actions = actions.ToArray();
            return state;
        }

        /// <summary>
        /// 创建大丝球大招锁定状态
        /// </summary>
        private void CreateBigSilkBallLockState()
        {
            if (_bossControlFsm == null) return;

            Log.Info("创建大丝球大招锁定状态");

            // 使用 FsmStateBuilder 创建并添加锁定状态
            var lockState = CreateAndAddState(_bossControlFsm, "Big Silk Ball Lock", "大丝球大招期间锁定BOSS，只播放Idle动画");
            lockState.Actions = Array.Empty<FsmStateAction>();

            // 添加转换：只监听BIG SILK BALL UNLOCK事件
            var idleState = FindState(_bossControlFsm, "Idle");
            if (idleState != null)
            {
                lockState.Transitions = new FsmTransition[]
                {
                CreateTransition(FsmEvent.GetFsmEvent("BIG SILK BALL UNLOCK"), idleState)
                };
            }

            // 重新初始化FSM
            // ReinitializeFsm(_bossControlFsm);

            Log.Info("大丝球大招锁定状态创建完成");
        }

        /// <summary>
        /// 添加全局事件监听（在任意状态都可以响应）
        /// </summary>
        private void AddGlobalEventListeners()
        {
            if (_bossControlFsm == null) return;

            var idleState = FindState(_bossControlFsm, "Idle");
            if (idleState == null)
            {
                Log.Error("未找到Idle状态，跳过添加FORCE IDLE全局监听");
                return;
            }

            var lockState = FindState(_bossControlFsm, "Big Silk Ball Lock");
            if (lockState == null)
            {
                Log.Warn("未找到Big Silk Ball Lock状态，跳过添加BIG SILK BALL LOCK全局监听");
            }

            // 添加全局转换
            var globalTransitions = _bossControlFsm.FsmGlobalTransitions.ToList();

            // 添加FORCE IDLE全局转换
            globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("FORCE IDLE"), idleState));

            // 添加BIG SILK BALL LOCK全局转换
            if (lockState != null)
            {
                globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("BIG SILK BALL LOCK"), lockState));
            }
            _bossControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
        }

        /// <summary>
        /// 创建Dash到指定点位的状态（使用FaceObjectV2面向目标，Tk2dPlayAnimationWithEvents播放Dash动画）
        /// </summary>
        private FsmState CreateDashToPointState(int pointIndex)
        {
            string stateName = pointIndex == -1 ? "Dash To Special" : $"Dash To Point {pointIndex}";
            string description = pointIndex == -1 ? "冲刺到Special点位" : $"冲刺到点位 {pointIndex}";

            var state = CreateState(_bossControlFsm!.Fsm, stateName, description);

            var actions = new List<FsmStateAction>();

            // 获取目标坐标
            FsmFloat? targetX = null;
            FsmFloat? targetY = null;

            switch (pointIndex)
            {
                case -1:  // Special点位
                    targetX = RoutePointSpecialX;
                    targetY = RoutePointSpecialY;
                    break;
                case 0:
                    targetX = RoutePoint0X;
                    targetY = RoutePoint0Y;
                    // Point 0 需要取消硬直
                    actions.Add(new SendEventByName
                    {
                        eventTarget = new FsmEventTarget
                        {
                            target = FsmEventTarget.EventTarget.GameObject,
                            excludeSelf = new FsmBool(false),
                            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner }
                        },
                        sendEvent = new FsmString("STUN CONTROL STOP") { Value = "STUN CONTROL STOP" },
                        delay = new FsmFloat(0f),
                        everyFrame = false
                    });
                    break;
                case 1:
                    targetX = RoutePoint1X;
                    targetY = RoutePoint1Y;
                    break;
                case 2:
                    targetX = RoutePoint2X;
                    targetY = RoutePoint2Y;
                    break;
            }

            if (targetX == null || targetY == null)
            {
                Log.Error($"CreateDashToPointState: Route Point for point {pointIndex} is null");
                return state;
            }

            // 1. 设置Y速度为0（防止掉落）
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new Vector2(0f, 0f),
                x = new FsmFloat { UseVariable = false },
                y = new FsmFloat(0f),
                everyFrame = false
            });

            // 2. 播放Dash动画（fps已在Dash Antic状态中动态调整）
            actions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Dash") { Value = "Dash" },
                animationTriggerEvent = null,
                animationCompleteEvent = FsmEvent.Finished // 动画播放完成后触发FINISHED
            });

            // 3. 移动到目标位置（使用速度模式，与动画同步）
            actions.Add(new AnimateXPositionTo
            {
                GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                ToValue = targetX,
                localSpace = false,
                time = new FsmFloat(MAX_DASH_TIME), // 最大时间作为超时保护
                speed = new FsmFloat(DASH_SPEED), // 使用固定速度
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false
            });

            actions.Add(new AnimateYPositionTo
            {
                GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                ToValue = targetY,
                localSpace = false,
                time = new FsmFloat(MAX_DASH_TIME), // 最大时间作为超时保护
                speed = new FsmFloat(DASH_SPEED), // 使用固定速度
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info($"创建Dash To Point {pointIndex}状态（动态计算时间和动画速度）");
            return state;
        }

        /// <summary>
        /// 创建在指定点位Idle等待的状态（Dash动画完成后进入，播放Idle动画并等待0.6秒）
        /// </summary>
        private FsmState CreateIdleAtPointState(int pointIndex)
        {
            string stateName = pointIndex == -1 ? "Idle At Special" : $"Idle At Point {pointIndex}";
            string description = pointIndex == -1 ? "在Special点位等待" : $"在点位 {pointIndex} 等待";

            var state = CreateState(_bossControlFsm!.Fsm, stateName, description);

            var actions = new List<FsmStateAction>();


            // 1. 面向英雄（到达点位后面向玩家更合理）
            if (HeroController.instance != null)
            {
                actions.Add(new FaceObjectV2
                {
                    objectA = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    objectB = HeroController.instance.gameObject,
                    spriteFacesRight = false,
                    playNewAnimation = false,
                    newAnimationClip = new FsmString("") { Value = "" },
                    resetFrame = false,
                    pauseBetweenTurns = 0f,
                    everyFrame = false
                });
            }

            // 2. 播放Idle动画（衔接Dash动画）
            actions.Add(new Tk2dPlayAnimation
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animLibName = new FsmString("") { Value = "" },
                clipName = new FsmString("Idle") { Value = "Idle" }
            });

            // 3. 等待可配置的时间后进入下一个点位（使用FsmFloat变量，可后续动态调整）
            actions.Add(new Wait
            {
                time = IdleWaitTime!,
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info($"创建Idle At Point {pointIndex}状态（面向英雄+Idle动画+等待）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Dash End状态（恢复硬直并通知AttackControl）
        /// </summary>
        private FsmState CreateSilkBallDashEndState()
        {
            var state = CreateState(_bossControlFsm!.Fsm, "Silk Ball Move End", "移动丝球结束，恢复硬直");

            var actions = new List<FsmStateAction>();

            // 1. 恢复硬直
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObject,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                },
                sendEvent = new FsmString("STUN CONTROL START") { Value = "STUN CONTROL START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 2. 通知AttackControl完成
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
                },
                sendEvent = new FsmString("SILK BALL DASH END") { Value = "SILK BALL DASH END" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });
            // 3. 播放Idle动画
            actions.Add(new Tk2dPlayAnimation
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animLibName = new FsmString("") { Value = "" },
                clipName = new FsmString("Idle") { Value = "Idle" }
            });

            // 4. 等待一小段时间再返回Idle状态
            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Dash End状态");
            return state;
        }

        #endregion

        #region Helper方法

        /// <summary>
        /// 创建隐形目标点GameObject（位置动态设置）
        /// </summary>
        private void CreateInvisibleTargetPoints()
        {
            // 创建隐形目标点，初始位置设为(0,0,0)
            _targetPointSpecial = new GameObject("DashTargetPointSpecial");
            _targetPointSpecial.transform.position = Vector3.zero;
            _targetPointSpecial.SetActive(true);

            _targetPoint0 = new GameObject("DashTargetPoint0");
            _targetPoint0.transform.position = Vector3.zero;
            _targetPoint0.SetActive(true); // 虽然隐形但需要激活

            _targetPoint1 = new GameObject("DashTargetPoint1");
            _targetPoint1.transform.position = Vector3.zero;
            _targetPoint1.SetActive(true);

            _targetPoint2 = new GameObject("DashTargetPoint2");
            _targetPoint2.transform.position = Vector3.zero;
            _targetPoint2.SetActive(true);

            Log.Info("已创建隐形目标点GameObject（包括Special）");
        }

        /// <summary>
        /// 更新隐形目标点的位置（由AttackControl调用）
        /// </summary>
        public void UpdateTargetPointPositions(Vector3 point0, Vector3 point1, Vector3 point2, Vector3? pointSpecial = null)
        {
            if (pointSpecial.HasValue && _targetPointSpecial != null)
            {
                _targetPointSpecial.transform.position = pointSpecial.Value;
            }

            if (_targetPoint0 != null)
            {
                _targetPoint0.transform.position = point0;
            }

            if (_targetPoint1 != null)
            {
                _targetPoint1.transform.position = point1;
            }

            if (_targetPoint2 != null)
            {
                _targetPoint2.transform.position = point2;
            }

            Log.Info($"已更新目标点位置: P0={point0}, P1={point1}, P2={point2}");
        }

        #endregion

        #region BlastBurst3 协作状态

        /// <summary>
        /// 创建 BlastBurst3 与 BossControl 协作的桥接状态
        /// 模仿 CLIMB CAST BRIDGE 的变量控制模式
        /// </summary>
        private void CreateBlastBurst3BridgeStates()
        {
            if (_bossControlFsm == null) return;

            Log.Info("=== 开始创建 BlastBurst3 协作状态 ===");

            // 1. 创建 InBurst3 布尔变量
            var inBurst3Var = EnsureBoolVariable(_bossControlFsm, "InBurst3");

            // 2. 注册事件
            var burst3BridgeEvent = FsmEvent.GetFsmEvent("BURST3 BRIDGE");
            var endBurst3Event = FsmEvent.GetFsmEvent("END BURST3");

            // 3. 创建状态
            var startBlastBurst3State = CreateAndAddState(_bossControlFsm, "StartBlastBurst3", "BlastBurst3开始，播放Appear动画");
            var endBlastBurst3State = CreateAndAddState(_bossControlFsm, "EndBlastBurst3", "BlastBurst3结束，播放TurnToIdle动画");

            // 4. 添加 StartBlastBurst3 状态的动作
            var startActions = new List<FsmStateAction>();

            // 设置 InBurst3 为 false
            startActions.Add(new SetBoolValue
            {
                boolVariable = inBurst3Var,
                boolValue = new FsmBool(false)
            });

            // 播放 Appear 动画
            startActions.Add(new Tk2dPlayAnimation
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animLibName = new FsmString("") { Value = "" },
                clipName = new FsmString("Appear") { Value = "Appear" }
            });
            startActions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new FsmVector2 { Value = Vector2.zero },
                x = new FsmFloat { Value = 0f },
                y = new FsmFloat { Value = 0f }
            });
            startBlastBurst3State.Actions = startActions.ToArray();

            // 5. 添加 EndBlastBurst3 状态的动作
            var endActions = new List<FsmStateAction>();

            // 播放 TurnToIdle 动画，动画完成后触发 FINISHED
            endActions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("TurnToIdle") { Value = "TurnToIdle" },
                animationCompleteEvent = FsmEvent.Finished
            });
            endActions.Add(new Wait { time = new FsmFloat(0.2f),finishEvent = FsmEvent.Finished });
            endBlastBurst3State.Actions = endActions.ToArray();

            // 6. 设置状态转换
            // StartBlastBurst3 收到 END BURST3 事件后进入 EndBlastBurst3
            startBlastBurst3State.Transitions = new FsmTransition[]
            {
                CreateTransition(endBurst3Event, endBlastBurst3State)
            };

            // EndBlastBurst3 的 FINISHED 跳转回 Idle
            var idleState = FindState(_bossControlFsm, "Idle");
            if (idleState != null)
            {
                SetFinishedTransition(endBlastBurst3State, idleState);
            }

            Log.Info("BlastBurst3 协作状态创建完成");
        }

        /// <summary>
        /// 在 Idle 状态添加 InBurst3 的 BoolTest（在 SetupControlIdlePendingTransitions 中调用）
        /// </summary>
        private void AddBurst3BoolTestToIdle(FsmState idleState, List<FsmStateAction> actions)
        {
            var startBlastBurst3State = FindState(_bossControlFsm!, "StartBlastBurst3");
            if (startBlastBurst3State == null)
            {
                Log.Warn("未找到 StartBlastBurst3 状态，跳过 InBurst3 BoolTest 添加");
                return;
            }

            var inBurst3Var = EnsureBoolVariable(_bossControlFsm!, "InBurst3");
            var burst3BridgeEvent = FsmEvent.GetFsmEvent("BURST3 BRIDGE");

            // 在现有 BoolTest 后面插入 InBurst3 的检测
            actions.Insert(2, new BoolTest
            {
                boolVariable = inBurst3Var,
                isTrue = burst3BridgeEvent,
                isFalse = FsmEvent.GetFsmEvent("NULL"),
                everyFrame = true
            });

            // 添加转换
            var transitions = idleState.Transitions?.ToList() ?? new List<FsmTransition>();
            transitions.RemoveAll(t => t.FsmEvent == burst3BridgeEvent);
            transitions.Add(new FsmTransition
            {
                FsmEvent = burst3BridgeEvent,
                toState = startBlastBurst3State.Name,
                toFsmState = startBlastBurst3State
            });
            idleState.Transitions = transitions.ToArray();

            Log.Info("已在 Idle 状态添加 InBurst3 BoolTest 和转换");
        }

        #endregion
    }
}