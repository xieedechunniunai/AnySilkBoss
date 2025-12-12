using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    internal partial class MemoryAttackControlBehavior
    {
        #region 原版AttackControl调整
        protected virtual void PatchOriginalAttackPatterns()
        {
            if (_strandPatterns == null)
            {
                Log.Warn("_strandPatterns为null，无法调整原版攻击Pattern！");
                return;
            }
            var pattern1 = _strandPatterns.transform.Find("Pattern 1");
            if (pattern1 == null)
            {
                Log.Warn("未找到 Pattern 1，原版攻击调整跳过");
                return;
            }
            if (_strandPatterns.transform.Find("Pattern 3") != null)
            {
                Log.Info("Pattern 3 已存在，无需再次复制");
                return;
            }
            // 复制 Pattern 1 为 Pattern 3 到 Pattern 10
            int copiedCount = 0;
            for (int i = 3; i <= 10; i++)
            {
                string patternName = $"Pattern {i}";
                if (_strandPatterns.transform.Find(patternName) != null)
                {
                    Log.Info($"{patternName} 已存在，跳过复制");
                    continue;
                }
                var patternObj = GameObject.Instantiate(pattern1.gameObject, _strandPatterns.transform);
                patternObj.name = patternName;
                copiedCount++;
            }

            Log.Info($"梦境版已自动复制Pattern 1为{copiedCount}份（Pattern 3 到 Pattern 10）");

            // 调用补丁方法
            PatchSinglePathRandomCombo();
            PatchSingleAndDoubleStatesLastActionsV2();
            PatchAllPatternsWebBurstStartDelay();
        }
        private void PatchSinglePathRandomCombo()
        {
            if (_attackControlFsm == null) return;

            var singleState = FindState(_attackControlFsm, "Single");
            var webRecoverState = FindState(_attackControlFsm, "Web Recover");
            if (singleState == null || webRecoverState == null)
            {
                Log.Warn("Single或Web Recover状态不存在，无法补丁梦境版Single连击");
                return;
            }

            // === 1. 修改Single状态，添加第2次攻击动作 ===
            var atkAction1 = CloneAction<GetRandomChild>("Double");
            var sendEventAction1 = CloneAction<SendEventByName>("Double");
            if (atkAction1 != null && sendEventAction1 != null)
            {
                sendEventAction1.delay = new FsmFloat(ATTACK_SEND_DELAY);
                var actions = singleState.Actions.ToList();
                actions.Add(atkAction1);
                actions.Add(sendEventAction1);
                singleState.Actions = actions.ToArray();
                Log.Info("已为Single状态添加第2次攻击动作");
            }

            // === 2. 创建事件 ===
            var singleExtraEvent = GetOrCreateEvent(_attackControlFsm, "SINGLE EXTRA");

            // === 3. 创建 Single Extra Check 状态 (随机判断) ===
            var singleExtraCheckState = CreateState(_attackControlFsm.Fsm, "Single Extra Check", "梦境版Single第3次攻击随机判断");
            singleExtraCheckState.Actions = new FsmStateAction[]
            {
                new SendRandomEventV4
                {
                    events = new FsmEvent[] { FsmEvent.Finished, singleExtraEvent },
                    weights = new FsmFloat[] { new FsmFloat(0.6f), new FsmFloat(0.4f) },
                    eventMax = new FsmInt[] { new FsmInt(2), new FsmInt(2) },
                    missedMax = new FsmInt[] { new FsmInt(99), new FsmInt(99) },
                    activeBool = new FsmBool { UseVariable = true, Value = true }
                }
            };

            // === 4. 创建 Single Extra 状态 (第3次攻击) ===
            var atkAction2 = CloneAction<GetRandomChild>("Double");
            var sendEventAction2 = CloneAction<SendEventByName>("Double");
            if (atkAction2 == null || sendEventAction2 == null)
            {
                Log.Warn("Single Extra: 克隆攻击动作失败");
                return;
            }
            sendEventAction2.delay = new FsmFloat(ATTACK_SEND_DELAY);

            var singleExtraState = CreateState(_attackControlFsm.Fsm, "Single Extra", "梦境版Single第3次攻击");
            singleExtraState.Actions = new FsmStateAction[] { atkAction2, sendEventAction2 };

            // === 5. 将新状态添加到FSM ===
            AddStatesToFsm(_attackControlFsm, singleExtraCheckState, singleExtraState);

            // === 6. 修改Single的跳转：原来到Web Recover改为到Single Extra Check ===
            foreach (var trans in singleState.Transitions)
            {
                if (trans.toState == "Web Recover" || trans.toFsmState == webRecoverState)
                {
                    trans.toState = singleExtraCheckState.Name;
                    trans.toFsmState = singleExtraCheckState;
                }
            }

            // === 7. 设置跳转关系 ===
            singleExtraCheckState.Transitions = new FsmTransition[]
            {
                CreateTransition(singleExtraEvent, singleExtraState),
                CreateFinishedTransition(webRecoverState)
            };
            SetFinishedTransition(singleExtraState, webRecoverState);

            Log.Info("梦境版Single路径已补丁：随机2次(0.6)或3次(0.4)攻击");
        }

        /// <summary>
        /// 遍历所有Pattern，在其silk_boss_pattern_control的Web Burst Start状态开头插入Wait延迟
        /// </summary>
        private void PatchAllPatternsWebBurstStartDelay()
        {
            if (_strandPatterns == null)
            {
                Log.Warn("_strandPatterns为null，无法Patch Web Burst Start延迟");
                return;
            }

            int patchedCount = 0;
            foreach (Transform patternTransform in _strandPatterns.transform)
            {
                var patternControlFsm = FSMUtility.LocateMyFSM(patternTransform.gameObject, "silk_boss_pattern_control");
                if (patternControlFsm == null)
                {
                    Log.Warn($"Pattern {patternTransform.name} 未找到 silk_boss_pattern_control FSM");
                    continue;
                }

                var webBurstStartState = patternControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Web Burst Start");
                if (webBurstStartState == null)
                {
                    Log.Warn($"Pattern {patternTransform.name} 未找到 Web Burst Start 状态");
                    continue;
                }

                var reparentEvent = FsmEvent.GetFsmEvent("REPARENT");
                var waitAction = new Wait
                {
                    time = new FsmFloat(3.3f),
                    finishEvent = reparentEvent
                };

                var actionsList = webBurstStartState.Actions.ToList();
                actionsList.Add(waitAction);
                webBurstStartState.Actions = actionsList.ToArray();

                foreach (var trans in webBurstStartState.Transitions)
                {
                    if (trans.ToState == "Reparent" && trans.FsmEvent.Name == "FINISHED")
                    {
                        trans.FsmEvent = reparentEvent;
                    }
                }

                patternControlFsm.Fsm.InitData();
                patchedCount++;
            }

            Log.Info($"梦境版共为 {patchedCount} 个Pattern的Web Burst Start状态添加了延迟");
        }

        /// <summary>
        /// 梦境版Double路径随机连击：3次(0.5权重)或4次(0.5权重)
        /// 
        /// 状态流程：
        /// Double (第1+2次攻击) -> Triple (第3次攻击) -> Triple Extra Check (随机判断)
        ///   -> QUADRUPLE (0.5) -> Quadruple (第4次攻击) -> Web Recover
        ///   -> FINISHED (0.5) -> Web Recover
        /// </summary>
        private void PatchSingleAndDoubleStatesLastActionsV2()
        {
            if (_attackControlFsm == null) return;

            var doubleState = FindState(_attackControlFsm, "Double");
            var webRecoverState = FindState(_attackControlFsm, "Web Recover");
            if (doubleState == null || webRecoverState == null)
            {
                Log.Warn("Double或Web Recover状态不存在，无法补丁梦境版Double连击链");
                return;
            }

            // === 0. 修改Double状态中SendEventByName的delay ===
            if (doubleState.Actions != null)
            {
                int patchedCount = 0;
                foreach (var action in doubleState.Actions)
                {
                    if (action is SendEventByName sendAction)
                    {
                        sendAction.delay = new FsmFloat(ATTACK_SEND_DELAY);
                        patchedCount++;
                    }
                }
                if (patchedCount > 0)
                {
                    Log.Info($"已将Double状态中{patchedCount}个SendEventByName的delay调整为{ATTACK_SEND_DELAY}s");
                }
            }

            // === 1. 创建事件 ===
            var quadrupleEvent = GetOrCreateEvent(_attackControlFsm, "QUADRUPLE");

            // === 2. 创建 Triple 状态 (第3次攻击) ===
            var tripleAtkAction = CloneAction<GetRandomChild>("Double");
            var tripleSendEventAction = CloneAction<SendEventByName>("Double");
            if (tripleAtkAction == null || tripleSendEventAction == null)
            {
                Log.Warn("Triple: 克隆攻击动作失败");
                return;
            }
            tripleSendEventAction.delay = new FsmFloat(ATTACK_SEND_DELAY);

            var tripleState = CreateState(_attackControlFsm.Fsm, "Triple", "梦境版第3次攻击");
            tripleState.Actions = new FsmStateAction[]
            {
                tripleAtkAction,
                tripleSendEventAction
            };

            // === 3. 创建 Triple Extra Check 状态 (随机判断) ===
            var tripleExtraCheckState = CreateState(_attackControlFsm.Fsm, "Triple Extra Check", "梦境版第4次攻击随机判断");
            tripleExtraCheckState.Actions = new FsmStateAction[]
            {
                new SendRandomEventV4
                {
                    events = new FsmEvent[] { FsmEvent.Finished, quadrupleEvent },
                    weights = new FsmFloat[] { new FsmFloat(0.5f), new FsmFloat(0.5f) },
                    eventMax = new FsmInt[] { new FsmInt(2), new FsmInt(2) },
                    missedMax = new FsmInt[] { new FsmInt(99), new FsmInt(99) },
                    activeBool = new FsmBool { UseVariable = true, Value = true }
                }
            };

            // === 4. 创建 Quadruple 状态 (第4次攻击) ===
            var quadrupleAtkAction = CloneAction<GetRandomChild>("Double");
            var quadrupleSendEventAction = CloneAction<SendEventByName>("Double");
            if (quadrupleAtkAction == null || quadrupleSendEventAction == null)
            {
                Log.Warn("Quadruple: 克隆攻击动作失败");
                return;
            }
            quadrupleSendEventAction.delay = new FsmFloat(ATTACK_SEND_DELAY);

            var quadrupleState = CreateState(_attackControlFsm.Fsm, "Quadruple", "梦境版第4次攻击");
            quadrupleState.Actions = new FsmStateAction[]
            {
                quadrupleAtkAction,
                quadrupleSendEventAction,
                new Wait { time = new FsmFloat(1.0f), finishEvent = FsmEvent.Finished }
            };

            // === 5. 将所有新状态添加到FSM ===
            AddStatesToFsm(_attackControlFsm, tripleState, tripleExtraCheckState, quadrupleState);

            // === 6. 设置跳转关系 ===
            // Double -> Triple (把原来到 Web Recover 的跳转改成 Triple)
            foreach (var trans in doubleState.Transitions)
            {
                if (trans.toState == "Web Recover" || trans.toFsmState == webRecoverState)
                {
                    trans.toState = tripleState.Name;
                    trans.toFsmState = tripleState;
                }
            }

            // Triple -> Triple Extra Check
            SetFinishedTransition(tripleState, tripleExtraCheckState);

            // Triple Extra Check: QUADRUPLE -> Quadruple, FINISHED -> Web Recover
            tripleExtraCheckState.Transitions = new FsmTransition[]
            {
                CreateTransition(quadrupleEvent, quadrupleState),
                CreateFinishedTransition(webRecoverState)
            };

            // Quadruple -> Web Recover
            SetFinishedTransition(quadrupleState, webRecoverState);

            // === 7. 调整 Web Recover 和 Move Restart 的等待时间 ===
            PatchRecoverWaitTimes();

            Log.Info("梦境版Double路径已补丁：随机3次(0.5)或4次(0.5)攻击");
        }

        /// <summary>
        /// 调整 Web Recover 和 Move Restart 的等待时间
        /// </summary>
        private void PatchRecoverWaitTimes()
        {
            if (_attackControlFsm == null) return;

            var webRecoverState = FindState(_attackControlFsm, "Web Recover");
            if (webRecoverState?.Actions != null)
            {
                foreach (var action in webRecoverState.Actions)
                {
                    if (action is Wait waitAction)
                    {
                        var oldTime = waitAction.time.Value;
                        waitAction.time = new FsmFloat(0.5f);
                        Log.Info($"梦境版已将 Web Recover 的 Wait 时间从 {oldTime}s 调整为 0.5s");
                        break;
                    }
                }
            }

            var moveRestartState = FindState(_attackControlFsm, "Move Restart");
            if (moveRestartState?.Actions != null)
            {
                foreach (var action in moveRestartState.Actions)
                {
                    if (action is Wait waitAction)
                    {
                        var oldTime = waitAction.time.Value;
                        waitAction.time = new FsmFloat(1.0f);
                        Log.Info($"梦境版已将 Move Restart 的 Wait 时间从 {oldTime}s 调整为 1.0s");
                        break;
                    }
                }
            }
        }
        private void AddAttactStopAction()
        {
            var attackStopState = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Attack Stop");
            if (attackStopState == null) { return; }
            var actions = attackStopState.Actions.ToList();
            actions.Insert(0, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ClearSilkBallMethod") { Value = "ClearSilkBallMethod" },
                parameters = new FsmVar[0],
                everyFrame = false
            });
            attackStopState.Actions = actions.ToArray();
        }

        private void ModifyDashAttackState()
        {
            var dashAttackState = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Dash Attack");
            if (dashAttackState == null) { return; }
            var dashAttackAnticState = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Dash Attack Antic");
            if (dashAttackAnticState == null) { return; }

            var dashAttackactions = dashAttackAnticState.Actions.ToList();
            foreach (var action in dashAttackactions)
            {
                if (action is SendEventByName sendEventByName &&
                    sendEventByName.sendEvent?.Value != null &&
                    sendEventByName.sendEvent.Value.Contains("STOMP DASH"))
                {
                    sendEventByName.delay = new FsmFloat(0.28f);
                }
            }
            dashAttackAnticState.Actions = dashAttackactions.ToArray();

            var dashAttackEndState = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Dash Attack End");
            if (dashAttackEndState == null) { return; }

            var dashAttackEndactions = dashAttackEndState.Actions.ToList();

            if (laceCircleSlash != null)
            {
                dashAttackEndactions.Insert(0, new SpawnObjectFromGlobalPool
                {
                    gameObject = new FsmGameObject { Value = laceCircleSlash },
                    spawnPoint = new FsmGameObject { Value = this.gameObject },
                    position = new FsmVector3 { Value = Vector3.zero },
                    rotation = new FsmVector3 { Value = Vector3.zero },
                    storeObject = _laceSlashObj
                });
            }
            else
            {
                Log.Warn("laceCircleSlash 为 null，跳过 Dash Attack End 的斩击特效生成");
            }
            dashAttackEndState.Actions = dashAttackEndactions.ToArray();
        }

        private void ModifySpikeLiftAimState()
        {
            var spikeLiftAimState = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Spike Lift Aim");
            var spikeLiftAimState2 = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Spike Lift Aim 2");
            if (spikeLiftAimState == null || spikeLiftAimState2 == null || _bossScene == null) { return; }
            var spikeFloors = _bossScene.transform.Find("Spike Floors").gameObject;
            var spikeLiftAimactions = spikeLiftAimState.Actions.ToList();
            var spikeLiftAimactions2 = spikeLiftAimState2.Actions.ToList();
            spikeLiftAimactions.Add(new GetRandomChild
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = spikeFloors } },
                storeResult = _spikeFloorsX
            });
            spikeLiftAimactions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObject,
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = _spikeFloorsX
                    }
                },
                sendEvent = new FsmString { Value = "ATTACK" },
                delay = new FsmFloat { Value = 0.2f }
            });
            foreach (var action in spikeLiftAimactions)
            {
                if (action is Wait wait &&
                    wait.time?.Value != null)
                {
                    wait.time = new FsmFloat(0.3f);
                }
            }

            spikeLiftAimactions2.Add(new GetRandomChild
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = spikeFloors } },
                storeResult = _spikeFloorsX
            });
            spikeLiftAimactions2.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObject,
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = _spikeFloorsX
                    }
                },
                sendEvent = new FsmString { Value = "ATTACK" },
                delay = new FsmFloat { Value = 0.2f }
            });
            spikeLiftAimState.Actions = spikeLiftAimactions.ToArray();
            spikeLiftAimState2.Actions = spikeLiftAimactions2.ToArray();
        }

        public void ClearSilkBallMethod()
        {
            Log.Info("Boss眩晕，开始清理丝球");

            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                var silkBallManager = managerObj.GetComponent<SilkBallManager>();
                if (silkBallManager != null)
                {
                    silkBallManager.RecycleAllActiveSilkBalls();
                    Log.Info("已通过SilkBallManager清理所有丝球");
                }
                else
                {
                    Log.Warn("未找到SilkBallManager组件");
                }
            }
            else
            {
                Log.Warn("未找到AnySilkBossManager GameObject");
            }
            StopGeneratingSilkBall();
            ClearActiveSilkBalls();
        }
        #endregion
    }
}

