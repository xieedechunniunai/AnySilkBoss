using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    internal partial class MemoryAttackControlBehavior
    {
        private FsmEvent? _silkBallAttackEvent;
        private FsmEvent? _silkBallWithWebAttackEvent;
        private FsmEvent? _silkBallWithWebAttackDoneEvent;
        private SilkBallManager? _silkBallManager;
        private SingleWebManager? _singleWebManager;
        private List<GameObject> _activeSilkBalls = new List<GameObject>();
        private Coroutine? _silkBallSummonCoroutine;
        private Coroutine? _silkBallWithWebAttackCoroutine;

        private FsmState? _silkBallPrepareState;
        private FsmState? _silkBallRingPrepareState;
        private FsmState? _silkBallRingCastState;
        private FsmState? _silkBallRingLiftState;
        private FsmState? _silkBallRingAnticState;
        private FsmState? _silkBallRingReleaseState;
        private FsmState? _silkBallRingEndState;
        private FsmState? _silkBallRingRecoverState;
        private FsmState? _silkBallMovePrepareState;
        private FsmState? _silkBallMoveEndState;

        private FsmState? _silkBallWebPrepareState;
        private FsmState? _silkBallWebAttackState;
        private FsmState? _silkBallWebRecoverState;

        private FsmBool? _isGeneratingSilkBall;
        private FsmFloat? _totalDistanceTraveled;
        private FsmVector2? _lastBallPosition;
        private FsmGameObject? _laceSlashObj;
        private FsmGameObject? _spikeFloorsX;

        private FsmEvent? _silkBallStaticEvent;
        private FsmEvent? _silkBallDashEvent;
        private FsmEvent? _silkBallDashStartEvent;
        private FsmEvent? _silkBallDashEndEvent;

        private StartRoarEmitter? _cachedRoarEmitter;
        private PlayAudioEventRandom? _cachedPlayRoarAudio;

        #region 丝球攻击（静态分支）
        /// <summary>
        /// 创建丝球环绕攻击的所有状态
        /// </summary>
        private void CreateSilkBallAttackStates()
        {
            // 创建所有状态（静态版本 + 移动版本）
            _silkBallPrepareState = CreateSilkBallPrepareState();
            _silkBallRingPrepareState = CreateSilkBallRingPrepareState();
            _silkBallRingCastState = CreateSilkBallRingCastState();
            _silkBallRingLiftState = CreateSilkBallRingLiftState();
            _silkBallRingAnticState = CreateSilkBallRingAnticState();
            _silkBallRingReleaseState = CreateSilkBallRingReleaseState();
            _silkBallRingEndState = CreateSilkBallRingEndState();
            _silkBallRingRecoverState = CreateSilkBallRingRecoverState();

            // 创建移动版本的状态
            _silkBallMovePrepareState = CreateSilkBallMovePrepareState();
            _silkBallMoveEndState = CreateSilkBallMoveEndState();

            // 使用 FsmStateBuilder 批量添加状态
            // Silk Ball Prepare 为共享分支点（原版可能已存在），此处不重复添加
            AddStatesToFsm(_attackControlFsm!,
                _silkBallRingPrepareState, _silkBallRingCastState,
                _silkBallRingLiftState, _silkBallRingAnticState, _silkBallRingReleaseState,
                _silkBallRingEndState, _silkBallRingRecoverState,
                _silkBallMovePrepareState, _silkBallMoveEndState);

            // 查找状态用于链接
            var moveRestartState = _moveRestartState;
            if (moveRestartState == null)
            {
                Log.Error("未找到Move Restart状态");
                return;
            }

            // 设置状态转换（使用 CreateTransition 辅助方法）
            _silkBallPrepareState.Transitions = new FsmTransition[]
            {
                CreateTransition(_silkBallStaticEvent!, _silkBallRingPrepareState!),
                CreateTransition(_silkBallDashEvent!, _silkBallMovePrepareState!)
            };

            _silkBallMovePrepareState!.Transitions = new FsmTransition[]
            {
                CreateTransition(_silkBallDashEndEvent!, _silkBallMoveEndState!)
            };

            SetFinishedTransition(_silkBallMoveEndState!, _silkBallRingRecoverState!);
            SetFinishedTransition(_silkBallRingPrepareState!, _silkBallRingCastState!);

            _silkBallRingCastState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallRingLiftState!),
            };

            _silkBallRingLiftState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallRingAnticState!),
            };

            _silkBallRingAnticState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallRingReleaseState!),
            };

            _silkBallRingReleaseState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallRingEndState!),
            };

            SetFinishedTransition(_silkBallRingEndState!, _silkBallRingRecoverState!);
            SetFinishedTransition(_silkBallRingRecoverState!, moveRestartState!);

            var attackChoiceState = _attackChoiceState;
            if (attackChoiceState != null)
            {
                AddTransition(attackChoiceState, CreateTransition(_silkBallAttackEvent!, _silkBallPrepareState));
            }
        }

        /// <summary>
        /// 创建Silk Ball Prepare状态（50%静态 / 50%移动）
        /// </summary>
        private FsmState CreateSilkBallPrepareState()
        {
            var state = GetOrCreateState(_attackControlFsm!, "Silk Ball Prepare", "丝球攻击准备（50%分支）");

            var actions = new List<FsmStateAction>();
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" },

                },
                sendEvent = new FsmString("FORCE IDLE") { Value = "FORCE IDLE" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // Silk Ball Prepare: STATIC/DASH missedMax=3, WITH WEB 权重0.25 eventMax=1 missedMax=4
            actions.Add(new SendRandomEventV4
            {
                events = new FsmEvent[] { _silkBallStaticEvent!, _silkBallDashEvent!, _silkBallWithWebAttackEvent! },
                weights = new FsmFloat[] { 1f, 1f, 0.25f },
                eventMax = new FsmInt[] { 2, 2, 1 },
                missedMax = new FsmInt[] { 3, 3, 4 },
                activeBool = new FsmBool { UseVariable = true, Value = true }
            });

            state.Actions = actions.ToArray();

            return state;
        }

        /// <summary>
        /// 修改Attack Choice状态，添加丝球攻击
        /// 调整后的概率：HAND 53%, DASH 10%, WEB 12%, SILK BALL 25%
        /// </summary>
        private void ModifyAttackChoiceForSilkBall()
        {
            var attackChoiceState = _attackChoiceState;
            if (attackChoiceState == null)
            {
                Log.Error("未找到Attack Choice状态");
                return;
            }
            var sendRandomActions = new List<SendRandomEventV4>();
            for (int i = 0; i < attackChoiceState.Actions.Length; i++)
            {
                if (attackChoiceState.Actions[i] is SendRandomEventV4 sendRandomAction)
                {
                    sendRandomActions.Add(sendRandomAction);
                }
            }

            if (sendRandomActions.Count < 2)
            {
                Log.Warn($"找到的SendRandomEventV4动作数量不足（期望2个，实际{sendRandomActions.Count}个）");
                return;
            }

            // Attack Choice [3] (Can Web Strand Attack = true)
            // HAND: 0.53, missedMax 3 | DASH: 0.10, missedMax 5 | WEB: 0.12, missedMax 5 | SILK BALL: 0.25, eventMax 2, missedMax 4
            {
                var action = sendRandomActions[0];

                var newEvents = new FsmEvent[action.events.Length + 1];
                var newWeights = new FsmFloat[action.weights.Length + 1];
                var newEventMax = new FsmInt[action.eventMax.Length + 1];
                var newMissedMax = new FsmInt[action.missedMax.Length + 1];

                for (int i = 0; i < action.events.Length; i++)
                {
                    newEvents[i] = action.events[i];
                    newEventMax[i] = action.eventMax[i];

                    if (action.events[i] != null && action.events[i].Name == "HAND ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.53f);
                        newMissedMax[i] = new FsmInt(3);
                        Log.Info($"第一个动作 - HAND ATTACK: 权重 0.53, missedMax 3");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "DASH ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.10f);
                        newMissedMax[i] = new FsmInt(5);
                        Log.Info($"第一个动作 - DASH ATTACK: 权重 0.10, missedMax 5");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "WEB ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.12f);
                        newMissedMax[i] = new FsmInt(5);
                        Log.Info($"第一个动作 - WEB ATTACK: 权重 0.12, missedMax 5");
                    }
                    else
                    {
                        newWeights[i] = action.weights[i];
                        newMissedMax[i] = action.missedMax[i];
                    }
                }

                int newIndex = action.events.Length;
                newEvents[newIndex] = _silkBallAttackEvent!;
                newWeights[newIndex] = new FsmFloat(0.25f);
                newEventMax[newIndex] = new FsmInt(2);
                newMissedMax[newIndex] = new FsmInt(4);

                action.events = newEvents;
                action.weights = newWeights;
                action.eventMax = newEventMax;
                action.missedMax = newMissedMax;

                Log.Info("第一个SendRandomEventV4修改完成：HAND 0.53, DASH 0.10, WEB 0.12, SILK BALL 0.25");
            }

            // Attack Choice [4] (始终执行)
            // HAND: 0.6, missedMax 2 | DASH: 0.15, missedMax 4 | SILK BALL: 0.25, missedMax 3
            {
                var action = sendRandomActions[1];
                Log.Info("修改第二个SendRandomEventV4（按顺序判断）");

                var newEvents = new FsmEvent[action.events.Length + 1];
                var newWeights = new FsmFloat[action.weights.Length + 1];
                var newEventMax = new FsmInt[action.eventMax.Length + 1];
                var newMissedMax = new FsmInt[action.missedMax.Length + 1];

                for (int i = 0; i < action.events.Length; i++)
                {
                    newEvents[i] = action.events[i];
                    newEventMax[i] = action.eventMax[i];

                    if (action.events[i] != null && action.events[i].Name == "HAND ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.6f);
                        newMissedMax[i] = new FsmInt(2);
                        Log.Info($"第二个动作 - HAND ATTACK: 权重 0.6, missedMax 2");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "DASH ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.15f);
                        newMissedMax[i] = new FsmInt(4);
                        Log.Info($"第二个动作 - DASH ATTACK: 权重 0.15, missedMax 4");
                    }
                    else
                    {
                        newWeights[i] = action.weights[i];
                        newMissedMax[i] = action.missedMax[i];
                    }
                }

                int newIndex = action.events.Length;
                newEvents[newIndex] = _silkBallAttackEvent!;
                newWeights[newIndex] = new FsmFloat(0.25f);
                newEventMax[newIndex] = new FsmInt(1);
                newMissedMax[newIndex] = new FsmInt(3);

                action.events = newEvents;
                action.weights = newWeights;
                action.eventMax = newEventMax;
                action.missedMax = newMissedMax;

                Log.Info("第二个SendRandomEventV4修改完成：HAND 0.6, DASH 0.15, SILK BALL 0.25");
            }
        }

        private FsmState CreateSilkBallRingPrepareState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Ring Prepare", "丝球攻击Prepare Cast");

            var actions = new List<FsmStateAction>();
            var stopStunControl = CloneAction<SendEventByName>("Web Lift");
            if (stopStunControl != null)
            {
                actions.Add(stopStunControl);
            }
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                fsm = _bossControlFsm,
                variableName = new FsmString("Attack Prepare") { Value = "Attack Prepare" },
                setValue = new FsmBool(true),
                everyFrame = false
            });
            state.Actions = actions.ToArray();

            return state;
        }

        /// <summary>
        /// 创建Silk Ball Cast状态（播放Cast动画并向上移动）
        /// </summary>
        private FsmState CreateSilkBallRingCastState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Ring Cast", "丝球攻击Cast动画，同时向上移动");

            var actions = new List<FsmStateAction>();
            var setVelocity = CloneAction<SetVelocity2d>("Web Cast");
            if (setVelocity != null)
            {
                actions.Add(setVelocity);
            }
            actions.Add(new Tk2dPlayAnimationWithEventsV2
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Cast") { Value = "Cast" },
                animationTriggerEvent = FsmEvent.Finished,
                animationCompleteEvent = _nullEvent,
                animationInterruptedEvent = _nullEvent,
                _sprite = gameObject.transform.GetComponent<tk2dSpriteAnimator>(),
                hasExpectedClip = true,
                expectedClip = gameObject.transform.GetComponent<tk2dSpriteAnimator>().Library.clips.FirstOrDefault(s =>s.name == "Cast")
            });
            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Cast状态（Cast动画 + 向上移动）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Lift状态（只负责向上移动到高点）
        /// </summary>
        private FsmState CreateSilkBallRingLiftState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Ring Lift", "向上移动到高点");

            var actions = new List<FsmStateAction>();

            var setVelocityUp = CloneAction<SetVelocity2d>("Web Lift", matchIndex: 0);
            if (setVelocityUp != null)
            {
                actions.Add(setVelocityUp);
            }

            var decelerate = CloneAction<DecelerateV2>("Web Lift");
            if (decelerate != null)
            {
                actions.Add(decelerate);
            }

            actions.Add(new Tk2dWatchAnimationEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animationCompleteEvent = _nullEvent,
                animationTriggerEvent = FsmEvent.Finished,
            });

            var checkHeight = CloneAction<CheckYPositionV2>("Web Lift");
            if (checkHeight != null)
            {
                actions.Add(checkHeight);
            }

            var stopAtHeight = CloneAction<SetVelocity2dBool>("Web Lift");
            if (stopAtHeight != null)
            {
                actions.Add(stopAtHeight);
            }

            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Lift状态（只上升 + 2秒超时保护）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Antic状态（在高点召唤丝球）
        /// </summary>
        private FsmState CreateSilkBallRingAnticState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Ring Antic", "在高点召唤8个丝球");

            var actions = new List<FsmStateAction>();

            var setVelocity = CloneAction<SetVelocity2d>("Roar Antic", matchIndex: 0);
            if (setVelocity != null)
            {
                actions.Add(setVelocity);
            }

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartSilkBallSummonAtHighPoint") { Value = "StartSilkBallSummonAtHighPoint" },
                parameters = new FsmVar[0],
                everyFrame = false
            });
            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Antic状态（在高点召唤）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Release状态（释放丝球 + 冲击波，无Roar动画）
        /// </summary>
        private FsmState CreateSilkBallRingReleaseState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Ring Release", "释放所有丝球并触发冲击波");

            var actions = new List<FsmStateAction>();

            var releaseBallsAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ReleaseSilkBalls") { Value = "ReleaseSilkBalls" },
                parameters = new FsmVar[0],
                everyFrame = false
            };
            actions.Add(releaseBallsAction);

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Release状态（无Roar动画）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball End状态
        /// </summary>
        private FsmState CreateSilkBallRingEndState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Ring End", "丝球攻击结束");

            var actions = new List<FsmStateAction>();
            actions.Add(new Tk2dWatchAnimationEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animationCompleteEvent = FsmEvent.Finished,
                animationTriggerEvent = _nullEvent,
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball End状态");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Recover状态（简单下降，然后交给Move Restart恢复）
        /// </summary>
        private FsmState CreateSilkBallRingRecoverState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Ring Recover", "丝球攻击结束，简单下降");

            var actions = new List<FsmStateAction>();

            var sendIdle = CloneAction<SendEventByName>("Web Recover", predicate: action =>
            {
                var eventName = action.sendEvent?.Value;
                return !string.IsNullOrEmpty(eventName) && eventName.Equals("IDLE", StringComparison.OrdinalIgnoreCase);
            });
            if (sendIdle != null)
            {
                actions.Add(sendIdle);
            }

            var playIdleAnim = CloneAction<Tk2dPlayAnimation>("Web Recover");
            if (playIdleAnim != null)
            {
                actions.Add(playIdleAnim);
            }

            var setVelocityDown = CloneAction<SetVelocity2d>("Web Recover");
            if (setVelocityDown != null)
            {
                actions.Add(setVelocityDown);
            }

            var decelerate = CloneAction<DecelerateV2>("Web Recover");
            if (decelerate != null)
            {
                actions.Add(decelerate);
            }

            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Recover状态（下降后转Move Restart）");
            return state;
        }
        #endregion
    }
}

