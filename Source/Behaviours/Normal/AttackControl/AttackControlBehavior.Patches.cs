using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    internal partial class AttackControlBehavior
    {
        #region Dash Slash 相关常量和变量
        // Dash Slash 配置
        private const float DASH_SLASH_DISTANCE_THRESHOLD = 5f;  // 每移动7单位生成一个Slash
        private const float DASH_SLASH_SCALE_PHASE1 = 1.75f;     // 一阶段 End 时的 Slash 大小
        private const float DASH_SLASH_SCALE_PHASE2 = 1f;        // 二阶段移动时的 Slash 大小
        private const float DASH_SLASH_OFFSET_Y_ATTACK = 0f;     // Dash Attack 状态时的 Y 偏移
        private const float DASH_SLASH_OFFSET_Y_END = 0f;      // Dash Attack End 状态时的 Y 偏移

        // Dash Slash 状态变量
        private bool _isGeneratingDashSlash = false;
        private float _dashSlashDistanceTraveled = 0f;
        private Vector2 _lastDashSlashPosition = Vector2.zero;
        private bool _isDashAttackEndState = false;  // 标记当前是否在 Dash Attack End 状态
        #endregion

        #region 原版AttackControl调整
        private void PatchOriginalAttackPatterns()
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
            var pattern3Obj = GameObject.Instantiate(pattern1.gameObject, _strandPatterns.transform);
            pattern3Obj.name = "Pattern 3";
            Log.Info("已自动复制Pattern 1为Pattern 3");
            PatchSingleAndDoubleStatesLastActions();
            PatchSingleAndDoubleStatesLastActionsV2();
            PatchAllPatternsWebBurstStartDelay();
        }

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
                Log.Info($"已为 {patternTransform.name} 的 Web Burst Start 状态添加 Wait 1.5s");
            }

            Log.Info($"共为 {patchedCount} 个Pattern的Web Burst Start状态添加了延迟");
        }

        private void PatchSingleAndDoubleStatesLastActions()
        {
            if (_attackControlFsm == null) return;
            var singleState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Single");
            var doubleState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Double");
            if (singleState == null || doubleState == null)
            {
                Log.Warn("Single或Double状态不存在，无法补丁攻击行为补齐");
                return;
            }
            var dActions = doubleState.Actions;
            if (dActions == null || dActions.Length < 2)
            {
                Log.Warn("Double状态行为数量过少，无法补丁行为补齐");
                return;
            }
            var newLast1 = CloneAction<GetRandomChild>("Double");
            var newLast2 = CloneAction<SendEventByName>("Double");
            if (newLast1 == null || newLast2 == null)
            {
                Log.Warn("克隆Double最后两个行为失败，无法补丁行为补齐");
                return;
            }
            newLast2.delay = new FsmFloat(0.8f);
            var singleActions = singleState.Actions.ToList();
            singleActions.Add(newLast1);
            singleActions.Add(newLast2);
            singleState.Actions = singleActions.ToArray();
            Log.Info("已将Double最后GetRandomChild/SendEventByName行为复制到Single和Double末尾各一份");
        }

        private void PatchSingleAndDoubleStatesLastActionsV2()
        {
            if (_attackControlFsm == null) return;

            var doubleState = FindState(_attackControlFsm, "Double");
            var webRecoverState = FindState(_attackControlFsm, "Web Recover");
            if (doubleState == null || webRecoverState == null)
            {
                Log.Warn("Double或Web Recover状态不存在，无法补丁Triple攻击链");
                return;
            }

            var atkAction = CloneAction<GetRandomChild>("Double");
            var sendEventAction = CloneAction<SendEventByName>("Double");
            if (atkAction == null || sendEventAction == null)
            {
                Log.Warn("Double克隆攻击动作失败");
                return;
            }
            sendEventAction.delay = new FsmFloat(0.8f);

            var tripleState = CreateState(_attackControlFsm.Fsm, "Triple", "补丁三连击：1次GetRandomChild+SendEvent+延时1s");
            tripleState.Actions = new FsmStateAction[]
            {
                atkAction,
                sendEventAction,
                new Wait { time = new FsmFloat(1.0f), finishEvent = FsmEvent.Finished }
            };

            AddStateToFsm(_attackControlFsm, tripleState);

            foreach (var trans in doubleState.Transitions)
            {
                if (trans.toState == "Web Recover" || trans.toFsmState == webRecoverState)
                {
                    trans.toState = tripleState.Name;
                    trans.toFsmState = tripleState;
                }
            }

            SetFinishedTransition(tripleState, webRecoverState);

            Log.Info("已自动插入补丁Triple攻击链，并更新Double跳转");
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

            // 修改 Dash Attack 状态：添加开始生成 Slash 的调用
            var dashAttackActions = dashAttackState.Actions.ToList();
            dashAttackActions.Insert(0, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartGeneratingDashSlash") { Value = "StartGeneratingDashSlash" },
                parameters = new FsmVar[0],
                everyFrame = false
            });
            dashAttackState.Actions = dashAttackActions.ToArray();
            Log.Info("已修改 Dash Attack 状态，添加 StartGeneratingDashSlash 调用");

            var dashAttackEndState = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Dash Attack End");
            if (dashAttackEndState == null) { return; }

            var dashAttackEndactions = dashAttackEndState.Actions.ToList();

            // 使用 CallMethod 调用 OnDashAttackEnd 方法（处理一阶段/二阶段不同逻辑）
            dashAttackEndactions.Insert(0, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("OnDashAttackEnd") { Value = "OnDashAttackEnd" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 在状态结束时停止生成 Slash
            dashAttackEndactions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopGeneratingDashSlash") { Value = "StopGeneratingDashSlash" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            dashAttackEndState.Actions = dashAttackEndactions.ToArray();
            Log.Info("已修改 Dash Attack End 状态，添加 OnDashAttackEnd 和 StopGeneratingDashSlash 调用");
        }

        /// <summary>
        /// 开始生成 Dash Slash（在 Dash Attack 状态开始时调用）
        /// </summary>
        public void StartGeneratingDashSlash()
        {
            var specialAttackVar = _attackControlFsm?.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            // 只有二阶段才启用移动生成 Slash
            if (isPhase2)
            {
                _isGeneratingDashSlash = true;
                _dashSlashDistanceTraveled = 0f;
                _lastDashSlashPosition = transform.position;
                _isDashAttackEndState = false;
                Log.Info("二阶段 Dash Attack：开始生成移动 Slash");
            }
        }

        /// <summary>
        /// Dash Attack End 状态开始时调用
        /// </summary>
        public void OnDashAttackEnd()
        {
            var specialAttackVar = _attackControlFsm?.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            if (isPhase2)
            {
                // 二阶段：标记进入 End 状态，继续生成移动 Slash（偏移量不同）
                _isDashAttackEndState = true;
                Log.Info("二阶段 Dash Attack End：继续生成移动 Slash（偏移量 0.1）");
            }
            else
            {
                // 一阶段：在 Boss 当前位置生成一个 1.75 倍大小的 Slash
                SpawnDashSlashAtCurrentPosition(DASH_SLASH_SCALE_PHASE1, 0f);
                Log.Info("一阶段 Dash Attack End：生成 1.75 倍 Slash");
            }
        }

        /// <summary>
        /// 停止生成 Dash Slash
        /// </summary>
        public void StopGeneratingDashSlash()
        {
            if (_isGeneratingDashSlash)
            {
                _isGeneratingDashSlash = false;
                _dashSlashDistanceTraveled = 0f;
                _lastDashSlashPosition = Vector2.zero;
                _isDashAttackEndState = false;
                Log.Info("停止生成 Dash Slash");
            }
        }

        /// <summary>
        /// 检查并生成 Dash Slash（在 Update 中调用）
        /// </summary>
        private void CheckAndSpawnDashSlash()
        {
            if (!_isGeneratingDashSlash) return;

            Vector2 currentPos = transform.position;

            if (_lastDashSlashPosition == Vector2.zero)
            {
                _lastDashSlashPosition = currentPos;
                return;
            }

            float distance = Vector2.Distance(currentPos, _lastDashSlashPosition);
            _dashSlashDistanceTraveled += distance;
            _lastDashSlashPosition = currentPos;

            if (_dashSlashDistanceTraveled >= DASH_SLASH_DISTANCE_THRESHOLD)
            {
                // 根据当前状态选择不同的 Y 偏移
                float yOffset = _isDashAttackEndState ? DASH_SLASH_OFFSET_Y_END : DASH_SLASH_OFFSET_Y_ATTACK;
                SpawnDashSlashAtCurrentPosition(DASH_SLASH_SCALE_PHASE2, yOffset);
                _dashSlashDistanceTraveled = 0f;
            }
        }

        /// <summary>
        /// 在当前位置生成 Dash Slash
        /// </summary>
        private void SpawnDashSlashAtCurrentPosition(float scaleMultiplier, float yOffset)
        {
            if (_laceCircleSlashManager == null)
            {
                Log.Warn("SpawnDashSlashAtCurrentPosition: _laceCircleSlashManager 为 null");
                return;
            }

            var spawnPosition = transform.position + new Vector3(0f, yOffset, 0f);
            bool success = _laceCircleSlashManager.SpawnLaceCircleSlash(spawnPosition, scaleMultiplier);

            if (success)
            {
                Log.Info($"生成 Dash Slash: 位置={spawnPosition}, 缩放={scaleMultiplier}x");
            }
        }

        /// <summary>
        /// 供 FSM CallMethod 调用的方法，用于生成 LaceCircleSlash（保留兼容性）
        /// </summary>
        public void SpawnLaceCircleSlashMethod()
        {
            // 此方法保留用于兼容性，实际逻辑已移至 OnDashAttackEnd
            // 如果需要在其他地方调用，可以使用此方法
            if (_laceCircleSlashManager == null)
            {
                Log.Warn("SpawnLaceCircleSlashMethod: _laceCircleSlashManager 为 null");
                return;
            }

            var spawnPosition = transform.position;
            bool success = _laceCircleSlashManager.SpawnLaceCircleSlash(spawnPosition, DASH_SLASH_SCALE_PHASE1);
            
            if (success)
            {
                Log.Info($"SpawnLaceCircleSlashMethod: 成功在位置 {spawnPosition} 生成 LaceCircleSlash");
            }
            else
            {
                Log.Warn("SpawnLaceCircleSlashMethod: 生成 LaceCircleSlash 失败");
            }
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

            // 立即停止生成丝球（防止在清理过程中继续生成）
            StopGeneratingSilkBall();

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
            ClearActiveSilkBalls();
        }
        #endregion
    }
}

