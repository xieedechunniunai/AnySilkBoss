using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    internal partial class MemoryAttackControlBehavior
    {
        #region 丝球攻击（静态分支）
        /// <summary>
        /// 创建丝球环绕攻击的所有状态
        /// </summary>
        private void CreateSilkBallAttackStates()
        {
            // 创建所有状态（静态版本 + 移动版本）
            _silkBallPrepareState = CreateSilkBallPrepareState();
            _silkBallPrepareCastState = CreateSilkBallPrepareCastState();
            _silkBallCastState = CreateSilkBallCastState();
            _silkBallLiftState = CreateSilkBallLiftState();
            _silkBallAnticState = CreateSilkBallAnticState();
            _silkBallReleaseState = CreateSilkBallReleaseState();
            _silkBallEndState = CreateSilkBallEndState();
            _silkBallRecoverState = CreateSilkBallRecoverState();

            // 创建移动版本的状态
            _silkBallDashPrepareState = CreateSilkBallDashPrepareState();
            _silkBallDashEndState = CreateSilkBallDashEndState();

            // 使用 FsmStateBuilder 批量添加状态
            AddStatesToFsm(_attackControlFsm!,
                _silkBallPrepareState, _silkBallPrepareCastState, _silkBallCastState,
                _silkBallLiftState, _silkBallAnticState, _silkBallReleaseState,
                _silkBallEndState, _silkBallRecoverState,
                _silkBallDashPrepareState, _silkBallDashEndState);

            // 查找状态用于链接
            var moveRestartState = FindState(_attackControlFsm, "Move Restart");

            // 设置状态转换（使用 CreateTransition 辅助方法）
            _silkBallPrepareState.Transitions = new FsmTransition[]
            {
                CreateTransition(_silkBallStaticEvent!, _silkBallPrepareCastState!),
                CreateTransition(_silkBallDashEvent!, _silkBallDashPrepareState!)
            };

            _silkBallDashPrepareState!.Transitions = new FsmTransition[]
            {
                CreateTransition(_silkBallDashEndEvent!, _silkBallDashEndState!),
                CreateFinishedTransition(moveRestartState!)  // 超时保护
            };

            SetFinishedTransition(_silkBallDashEndState!, _silkBallRecoverState!);
            SetFinishedTransition(_silkBallPrepareCastState!, _silkBallCastState!);

            _silkBallCastState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallLiftState!),
            };

            _silkBallLiftState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallAnticState!),
            };

            _silkBallAnticState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallReleaseState!),
            };

            _silkBallReleaseState!.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(_silkBallEndState!),
            };

            SetFinishedTransition(_silkBallEndState!, _silkBallRecoverState!);
            SetFinishedTransition(_silkBallRecoverState!, moveRestartState!);

            var attackChoiceState = FindState(_attackControlFsm, "Attack Choice");
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
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Prepare", "丝球攻击准备（50%分支）");

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

            actions.Add(new SendRandomEventV4
            {
                events = new FsmEvent[] { _silkBallStaticEvent!, _silkBallDashEvent! },
                weights = new FsmFloat[] { 1f, 1f },
                eventMax = new FsmInt[] { 2, 2 },
                missedMax = new FsmInt[] { 2, 2 },
                activeBool = new FsmBool { UseVariable = true, Value = true }
            });

            state.Actions = actions.ToArray();

            return state;
        }

        /// <summary>
        /// 修改Attack Choice状态，添加丝球攻击
        /// </summary>
        private void ModifyAttackChoiceForSilkBall()
        {
            var attackChoiceState = _attackControlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Attack Choice");
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
                    newMissedMax[i] = action.missedMax[i];

                    if (action.events[i] != null && action.events[i].Name == "HAND ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.5f);
                        Log.Info($"第一个动作 - HAND ATTACK权重: {action.weights[i].Value} -> 0.5f");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "DASH ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.15f);
                        Log.Info($"第一个动作 - DASH ATTACK权重: {action.weights[i].Value} -> 0.15f");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "WEB ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.15f);
                        Log.Info($"第一个动作 - WEB ATTACK权重: {action.weights[i].Value} -> 0.15f");
                    }
                    else
                    {
                        newWeights[i] = action.weights[i];
                    }
                }

                int newIndex = action.events.Length;
                newEvents[newIndex] = _silkBallAttackEvent!;
                newWeights[newIndex] = new FsmFloat(0.2f);
                newEventMax[newIndex] = new FsmInt(1);
                newMissedMax[newIndex] = new FsmInt(4);

                action.events = newEvents;
                action.weights = newWeights;
                action.eventMax = newEventMax;
                action.missedMax = newMissedMax;

                Log.Info("第一个SendRandomEventV4修改完成：HAND 0.5, DASH 0.15, WEB 0.15, SILK BALL 0.2");
            }

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
                    newMissedMax[i] = action.missedMax[i];

                    if (action.events[i] != null && action.events[i].Name == "HAND ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.6f);
                        Log.Info($"第二个动作 - HAND ATTACK权重: {action.weights[i].Value} -> 0.6f");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "DASH ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.2f);
                        Log.Info($"第二个动作 - DASH ATTACK权重: {action.weights[i].Value} -> 0.2f");
                    }
                    else
                    {
                        newWeights[i] = action.weights[i];
                    }
                }

                int newIndex = action.events.Length;
                newEvents[newIndex] = _silkBallAttackEvent!;
                newWeights[newIndex] = new FsmFloat(0.2f);
                newEventMax[newIndex] = new FsmInt(1);
                newMissedMax[newIndex] = new FsmInt(4);

                action.events = newEvents;
                action.weights = newWeights;
                action.eventMax = newEventMax;
                action.missedMax = newMissedMax;
            }
        }

        private FsmState CreateSilkBallPrepareCastState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Prepare Cast", "丝球攻击Prepare Cast");

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
        private FsmState CreateSilkBallCastState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Cast", "丝球攻击Cast动画，同时向上移动");

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
            });
            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Cast状态（Cast动画 + 向上移动）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Lift状态（只负责向上移动到高点）
        /// </summary>
        private FsmState CreateSilkBallLiftState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Lift", "向上移动到高点");

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
        private FsmState CreateSilkBallAnticState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Antic", "在高点召唤8个丝球");

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
        private FsmState CreateSilkBallReleaseState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Release", "释放所有丝球并触发冲击波");

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
        private FsmState CreateSilkBallEndState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball End", "丝球攻击结束");

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
        private FsmState CreateSilkBallRecoverState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Recover", "丝球攻击结束，简单下降");

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

