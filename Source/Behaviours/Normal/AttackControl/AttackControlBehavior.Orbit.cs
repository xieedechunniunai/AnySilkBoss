using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    internal partial class AttackControlBehavior
    {
        #region 针环绕攻击方法
        /// <summary>
        /// 初始化手部Behavior组件
        /// </summary>
        private void InitializeHandBehaviors()
        {
            // 查找Hand L和Hand R对象
            handL = GameObject.Find("Hand L");
            handR = GameObject.Find("Hand R");

            if (handL != null)
            {
                handLBehavior = handL.GetComponent<HandControlBehavior>();
                if (handLBehavior == null)
                {
                    handLBehavior = CreateHandBehavior(handL);
                }
            }
            else
            {
                Log.Warn("未找到Hand L对象");
            }

            if (handR != null)
            {
                handRBehavior = handR.GetComponent<HandControlBehavior>();
                if (handRBehavior == null)
                {
                    handRBehavior = CreateHandBehavior(handR);
                }
            }
            else
            {
                Log.Warn("未找到Hand R对象");
            }
        }

        /// <summary>
        /// 创建 Hand Behavior（子类可覆盖以创建不同版本）
        /// </summary>
        private HandControlBehavior CreateHandBehavior(GameObject handObj)
        {
            return handObj.AddComponent<HandControlBehavior>();
        }

        /// <summary>
        /// 配置环绕攻击参数（根据Special Attack状态）
        /// </summary>
        public void ConfigureOrbitAttack()
        {
            if (_attackControlFsm == null) return;

            var specialAttackVar = _attackControlFsm.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            if (isPhase2)
            {
                if (handLBehavior != null)
                {
                    handLBehavior.SetOrbitAttackConfig(1f, 0.4f);
                }
                if (handRBehavior != null)
                {
                    handRBehavior.SetOrbitAttackConfig(-1f, 0.4f);
                }
                Log.Info("Phase2环绕攻击配置：Hand L顺时针，Hand R逆时针，间隔0.4秒");
            }
            else
            {
                if (handLBehavior != null)
                {
                    handLBehavior.SetOrbitAttackConfig(1f, 0.5f);
                }
                if (handRBehavior != null)
                {
                    handRBehavior.SetOrbitAttackConfig(1f, 0.5f);
                }
                Log.Info("普通环绕攻击配置：双Hand顺时针，间隔0.5秒");
            }
        }

        /// <summary>
        /// 创建环绕攻击状态
        /// </summary>
        private void CreateOrbitAttackState()
        {
            // 使用 FsmStateBuilder 创建并添加状态
            _orbitAttackState = CreateAndAddState(_attackControlFsm!, newOrbitAttackState, "环绕攻击状态");

            // 添加动作到新状态
            AddOrbitAttackActions();
            // 创建子状态和添加转换
            CreateOrbitAttackSubStates();
        }

        /// <summary>
        /// 添加环绕攻击动作 - 新版本：拆分状态，使用FINISHED事件
        /// </summary>
        private void AddOrbitAttackActions()
        {
            // ⚠️ 在发送事件前，根据Special Attack设置Hand配置
            var configureHandsAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ConfigureOrbitAttack") { Value = "ConfigureOrbitAttack" },
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // Orbit Attack状态只负责发送初始事件并立即转换
            var sendToHandLAction = new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObject,
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = new FsmGameObject { Value = handL }
                    }
                },
                sendEvent = "ORBIT START Hand L",
                delay = new FsmFloat(0f),
                everyFrame = false
            };

            var sendToHandRAction = new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObject,
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = new FsmGameObject { Value = handR }
                    }
                },
                sendEvent = "ORBIT START Hand R",
                delay = new FsmFloat(0f),
                everyFrame = false
            };

            // 立即转换到等待状态
            var finishAction = new Wait
            {
                time = new FsmFloat(2f), // 很短的延迟确保事件发送完成
                finishEvent = FsmEvent.Finished
            };

            _orbitAttackState!.Actions = new FsmStateAction[] {
                configureHandsAction,
                sendToHandLAction,
                sendToHandRAction,
                finishAction
            };
        }

        /// <summary>
        /// 创建环绕攻击子状态
        /// </summary>
        private void CreateOrbitAttackSubStates()
        {
            if (_attackControlFsm == null) return;

            // 使用 FsmStateBuilder 批量创建子状态
            var orbitSubStates = CreateStates(_attackControlFsm.Fsm,
                ("Orbit First Shoot", "发送第一个SHOOT事件"),
                ("Orbit Second Shoot", "发送第二个SHOOT事件")
            );
            AddStatesToFsm(_attackControlFsm, orbitSubStates);

            var orbitFirstShootState = orbitSubStates[0];
            var orbitSecondShootState = orbitSubStates[1];

            // 设置各状态的动作
            SetOrbitFirstShootActions(orbitFirstShootState);
            SetOrbitSecondShootActions(orbitSecondShootState);
            // 添加转换
            AddOrbitAttackTransitions(orbitFirstShootState);
            // 设置状态转换
            if (_waitForHandsReadyState != null)
            {
                SetOrbitAttackSubStateTransitions(orbitFirstShootState, orbitSecondShootState, _waitForHandsReadyState);
            }
        }

        /// <summary>
        /// 设置Orbit First Shoot状态动作
        /// </summary>
        private void SetOrbitFirstShootActions(FsmState orbitFirstShootState)
        {
            var randomShootAction = new CallMethod
            {
                behaviour = this,
                methodName = "SendRandomShootEvent",
                parameters = new FsmVar[0]
            };

            var finishAction = new Wait
            {
                time = new FsmFloat(1.5f),
                finishEvent = FsmEvent.Finished
            };

            orbitFirstShootState.Actions = new FsmStateAction[] { randomShootAction, finishAction };
        }

        /// <summary>
        /// 设置Orbit Second Shoot状态动作
        /// </summary>
        private void SetOrbitSecondShootActions(FsmState orbitSecondShootState)
        {
            var secondShootAction = new CallMethod
            {
                behaviour = this,
                methodName = "SendSecondShootEvent",
                parameters = new FsmVar[0]
            };

            var finishAction = new Wait
            {
                time = new FsmFloat(4f),
                finishEvent = FsmEvent.Finished
            };

            orbitSecondShootState.Actions = new FsmStateAction[] { secondShootAction, finishAction };
        }

        /// <summary>
        /// 设置环绕攻击子状态转换
        /// </summary>
        private void SetOrbitAttackSubStateTransitions(FsmState orbitFirstShootState, FsmState orbitSecondShootState, FsmState waitForHandsReadyState)
        {
            // 使用 FsmStateBuilder 简化转换设置
            // Orbit First Shoot -> Orbit Second Shoot
            SetFinishedTransition(orbitFirstShootState, orbitSecondShootState);
            // Orbit Second Shoot -> Wait For Hands Ready
            SetFinishedTransition(orbitSecondShootState, waitForHandsReadyState);
        }

        /// <summary>
        /// 添加环绕攻击状态的转换
        /// </summary>
        private void AddOrbitAttackTransitions(FsmState orbitFirstShootState)
        {
            // 使用 AddTransition 添加到 Hand Ptn Choice
            AddTransition(_handPtnChoiceState!, CreateTransition(_orbitAttackEvent!, _orbitAttackState!));

            // 设置 Orbit Attack -> Orbit First Shoot
            SetFinishedTransition(_orbitAttackState!, orbitFirstShootState);
        }

        /// <summary>
        /// 修改HandPtnChoiceState状态的SendRandomEventV4动作，添加环绕攻击事件
        /// </summary>
        private void ModifySendRandomEventAction()
        {
            if (_handPtnChoiceState == null) return;
            var AllSendRandomEventActions = _handPtnChoiceState.Actions.OfType<SendRandomEventV4>().ToList();
            foreach (SendRandomEventV4 sendRandomEventAction in AllSendRandomEventActions)
            {
                var existingEvents = sendRandomEventAction.events;
                var existingWeights = sendRandomEventAction.weights;
                var existingEventMax = sendRandomEventAction.eventMax;
                var existingMissedMax = sendRandomEventAction.missedMax;
                var existingActiveBool = sendRandomEventAction.activeBool;
                // 创建新的事件数组（原有事件 + 新的环绕攻击事件）
                var newEvents = new FsmEvent[existingEvents.Length + 1];
                var newWeights = new FsmFloat[existingWeights.Length + 1];
                var newEventMax = new FsmInt[existingEventMax.Length + 1];
                var newMissedMax = new FsmInt[existingMissedMax.Length + 1];
                var newActiveBool = new FsmBool(existingActiveBool);
                // 复制原有事件
                for (int i = 0; i < existingEvents.Length; i++)
                {
                    newEvents[i] = existingEvents[i];
                    newWeights[i] = existingWeights[i];
                    newEventMax[i] = existingEventMax[i];
                    newMissedMax[i] = existingMissedMax[i];
                }

                // 添加新的环绕攻击事件
                int newIndex = existingEvents.Length;
                newEvents[newIndex] = FsmEvent.GetFsmEvent("ORBIT ATTACK");
                newWeights[newIndex] = new FsmFloat(2f); // 权重为2
                newEventMax[newIndex] = new FsmInt(1);   // 最大事件数为1
                newMissedMax[newIndex] = new FsmInt(4);  // 错过最大数为4
                newActiveBool = new FsmBool(existingActiveBool);
                // 更新动作的属性
                sendRandomEventAction.events = newEvents;
                sendRandomEventAction.weights = newWeights;
                sendRandomEventAction.eventMax = newEventMax;
                sendRandomEventAction.missedMax = newMissedMax;
                sendRandomEventAction.activeBool = newActiveBool;
            }
        }

        /// <summary>
        /// 随机选择Hand发送SHOOT事件
        /// </summary>
        public void SendRandomShootEvent()
        {
            Log.Info("随机选择Hand发送SHOOT事件");

            // 随机选择Hand L或Hand R
            bool chooseHandL = UnityEngine.Random.Range(0, 2) == 0;
            string selectedHand = chooseHandL ? "Hand L" : "Hand R";
            string otherHand = chooseHandL ? "Hand R" : "Hand L";

            Log.Info($"选择 {selectedHand} 作为第一个攻击的Hand");

            // 发送SHOOT事件给选中的Hand
            var selectedHandObject = chooseHandL ? handL : handR;
            if (selectedHandObject != null)
            {
                var handFSM = selectedHandObject.GetComponent<PlayMakerFSM>();
                if (handFSM != null)
                {
                    handFSM.SendEvent($"SHOOT {selectedHand}");
                    Log.Info($"已发送SHOOT事件给 {selectedHand}");
                }
            }

            // 保存另一个Hand的信息，用于后续发送
            _secondHandName = otherHand;
            _secondHandObject = chooseHandL ? handR : handL;
        }

        /// <summary>
        /// 发送SHOOT事件给第二个Hand
        /// </summary>
        public void SendSecondShootEvent()
        {
            Log.Info($"发送SHOOT事件给第二个Hand: {_secondHandName}");

            if (_secondHandObject != null)
            {
                var handFSM = _secondHandObject.GetComponent<PlayMakerFSM>();
                if (handFSM != null)
                {
                    handFSM.SendEvent($"SHOOT {_secondHandName}");
                    Log.Info($"已发送SHOOT事件给 {_secondHandName}");
                }
            }
        }
        #endregion
    }
}

