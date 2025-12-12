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
    internal partial class MemoryPhaseControlBehavior : MonoBehaviour
    {
        #region 大丝球大招相关
        /// <summary>
        /// 获取BigSilkBallManager引用
        /// </summary>
        private void GetBigSilkBallManager()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _bigSilkBallManager = managerObj.GetComponent<Managers.BigSilkBallManager>();
                if (_bigSilkBallManager != null)
                {
                    Log.Info("PhaseControl: 成功获取 BigSilkBallManager");
                }
                else
                {
                    Log.Warn("PhaseControl: 未找到 BigSilkBallManager 组件");
                }
            }
        }

        /// <summary>
        /// 在Phase Control FSM中添加大丝球大招状态序列
        /// 插入在 Set P3 Web Strand 之后，检查血量决定是否触发大招
        /// </summary>
        private void AddBigSilkBallStates()
        {
            if (_phaseControl == null)
            {
                Log.Error("Phase Control FSM 未初始化，无法添加大招状态");
                return;
            }

            Log.Info("=== 开始添加大丝球大招状态序列 ===");

            // 找到关键状态
            var setP3WebStrandState = FindState(_phaseControl, "Set P3 Web Strand");
            var p3State = FindState(_phaseControl, "P3");

            if (setP3WebStrandState == null)
            {
                Log.Error("未找到 Set P3 Web Strand 状态");
                return;
            }
            if (p3State == null)
            {
                Log.Error("未找到 P3 状态");
                return;
            }

            // 注册新事件
            RegisterBigSilkBallEvents();

            // 批量创建大丝球大招状态序列
            var bigSilkBallStates = CreateStates(_phaseControl.Fsm,
                ("P2.5", "P2.5阶段：监听TOOK DAMAGE触发血量检查"),
                ("HP Check 2.5", "检查血量：<=200触发大招，>200回到P2.5"),
                ("Big Silk Ball Roar", "大招前怒吼"),
                ("Big Silk Ball Roar End", "大招怒吼结束"),
                ("Big Silk Ball Prepare", "大招准备：停止攻击、设置无敌"),
                ("Big Silk Ball Move To Center", "Boss移动到中间高处"),
                ("Big Silk Ball Spawn", "生成大丝球并开始蓄力"),
                ("Big Silk Ball Wait", "等待大丝球爆炸和小丝球生成"),
                ("Big Silk Ball End", "大招结束：清理和恢复Layer"),
                ("Big Silk Ball Return", "BOSS从背景返回前景")
            );

            // 解构状态引用
            var p25State = bigSilkBallStates[0];
            var hpCheck25State = bigSilkBallStates[1];
            var bigSilkBallRoarState = bigSilkBallStates[2];
            var bigSilkBallRoarEndState = bigSilkBallStates[3];
            var bigSilkBallPrepareState = bigSilkBallStates[4];
            var bigSilkBallMoveToCenterState = bigSilkBallStates[5];
            var bigSilkBallSpawnState = bigSilkBallStates[6];
            var bigSilkBallWaitState = bigSilkBallStates[7];
            var bigSilkBallEndState = bigSilkBallStates[8];
            var bigSilkBallReturnState = bigSilkBallStates[9];

            // 添加状态到FSM
            AddStatesToFsm(_phaseControl, bigSilkBallStates);

            // 修改 Set P3 Web Strand 的跳转：改为跳到P2.5
            SetFinishedTransition(setP3WebStrandState, p25State);
            Log.Info("已修改 Set P3 Web Strand -> P2.5");

            // 添加状态动作
            AddP25Actions(p25State);
            AddHPCheck25Actions(hpCheck25State);
            // AddBigSilkBallRoarActions 延迟执行，等待 AttackControlBehavior 初始化
            AddBigSilkBallRoarEndActions(bigSilkBallRoarEndState);
            AddBigSilkBallPrepareActions(bigSilkBallPrepareState);
            AddBigSilkBallMoveToCenterActions(bigSilkBallMoveToCenterState);
            AddBigSilkBallSpawnActions(bigSilkBallSpawnState);
            AddBigSilkBallWaitActions(bigSilkBallWaitState);
            AddBigSilkBallEndActions(bigSilkBallEndState);
            AddBigSilkBallReturnActions(bigSilkBallReturnState);

            // 添加状态转换
            AddP25Transitions(p25State, hpCheck25State);
            AddHPCheck25Transitions(hpCheck25State, p25State, bigSilkBallRoarState);
            AddBigSilkBallTransitions(bigSilkBallPrepareState, bigSilkBallMoveToCenterState,
                bigSilkBallSpawnState, bigSilkBallWaitState, bigSilkBallEndState, bigSilkBallReturnState, p3State,
                bigSilkBallRoarState, bigSilkBallRoarEndState);

            // 延迟添加 Big Silk Ball Roar 状态的动作（等待 AttackControlBehavior 初始化）
            StartCoroutine(DelayedAddBigSilkBallRoarActions(bigSilkBallRoarState));

            Log.Info("=== 大丝球大招状态序列添加完成 ===");
        }

        /// <summary>
        /// 添加P2.5状态的动作（无动作，只监听TOOK DAMAGE）
        /// </summary>
        private void AddP25Actions(FsmState p25State)
        {
            // P2.5状态不需要动作，只监听TOOK DAMAGE事件
            p25State.Actions = new FsmStateAction[0];
        }

        /// <summary>
        /// 添加HP Check 2.5状态的动作
        /// </summary>
        private void AddHPCheck25Actions(FsmState hpCheck25State)
        {
            var actions = new List<FsmStateAction>();

            // 使用CompareHP检查血量是否<=200
            var selfGameObject = new FsmGameObject("Self");
            selfGameObject.Value = gameObject;

            var compareHP = new CompareHP
            {
                enemy = selfGameObject,
                integer2 = new FsmInt(200),  // 检查血量是否<=200
                lessThan = FsmEvent.GetFsmEvent("START BIG SILK BALL"),      // <200触发大招
                equal = FsmEvent.GetFsmEvent("START BIG SILK BALL"),         // =200触发大招
                greaterThan = FsmEvent.Finished,               // >200回到P2.5
                everyFrame = false
            };

            actions.Add(compareHP);
            hpCheck25State.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加P2.5状态的转换
        /// </summary>
        private void AddP25Transitions(FsmState p25State, FsmState hpCheck25State)
        {
            // P2.5监听TOOK DAMAGE事件，跳转到HP Check 2.5
            p25State.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("TOOK DAMAGE"), hpCheck25State)
            };
        }

        /// <summary>
        /// 添加HP Check 2.5状态的转换
        /// </summary>
        private void AddHPCheck25Transitions(FsmState hpCheck25State, FsmState p25State, FsmState bigSilkBallRoarState)
        {
            // FINISHED -> P2.5 (血量>200)
            // START BIG SILK BALL -> Big Silk Ball Roar (血量<=200，触发大招)
            hpCheck25State.Transitions = new FsmTransition[]
            {
                CreateFinishedTransition(p25State),
                CreateTransition(FsmEvent.GetFsmEvent("START BIG SILK BALL"), bigSilkBallRoarState)
            };
        }

        /// <summary>
        /// 添加准备状态的动作
        /// </summary>
        private void AddBigSilkBallPrepareActions(FsmState prepareState)
        {
            var actions = new List<FsmStateAction>();

            // 0. 保存Boss原始图层
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SaveOriginalLayer") { Value = "SaveOriginalLayer" },
                parameters = new FsmVar[0]
            });

            // 0.5. 禁用haze子物品
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DisableBossHaze") { Value = "DisableBossHaze" },
                parameters = new FsmVar[0]
            });

            // 1. Boss回血（释放大招时恢复血量）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("AddBossHealth") { Value = "AddBossHealth" },
                parameters = new FsmVar[]
            {
                new FsmVar(typeof(int)) { intValue = 200 }
            },
            });

            // 2. 生成大丝球（在准备阶段生成，这样可以跟随BOSS移动）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SpawnBigSilkBall") { Value = "SpawnBigSilkBall" },
                parameters = new FsmVar[0]
            });

            // 3. 发送 ATTACK STOP 事件到 Attack Control FSM（停止所有攻击）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
                },
                sendEvent = new FsmString("ATTACK STOP") { Value = "ATTACK STOP" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 4. 发送 BIG SILK BALL LOCK 事件到 Boss Control FSM（锁定BOSS）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("BIG SILK BALL LOCK") { Value = "BIG SILK BALL LOCK" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 5. 设置Boss无敌（Layer 2 = Invincible）
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = 2  // 无敌层，防止受到伤害
            });

            // 6. 等待0.5秒后进入移动状态
            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            });


            actions.Insert(0, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SendSilkStunnedToAllFingerBlades") { Value = "SendSilkStunnedToAllFingerBlades" },
                parameters = new FsmVar[] { }
            });
            prepareState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加移动到中心状态的动作
        /// </summary>
        private void AddBigSilkBallMoveToCenterActions(FsmState moveToCenterState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 保存BOSS原始Z轴和Scale
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SaveBossOriginalState") { Value = "SaveBossOriginalState" },
                parameters = new FsmVar[0]
            });

            // 2. 设置速度为0（防止重力影响）
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new Vector2(0f, 0f),
                x = new FsmFloat { UseVariable = false },
                y = new FsmFloat(0f),
                everyFrame = false
            });

            // 3. 播放动画（可选，使用 Drift F 动画）
            actions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Drift F") { Value = "Drift F" },
                animationCompleteEvent = null,
                animationTriggerEvent = null
            });

            // 4. 移动到中心X位置（65.5）
            actions.Add(new AnimateXPositionTo
            {
                GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                ToValue = new FsmFloat(39.5f),  // 房间中心X
                localSpace = false,
                time = new FsmFloat(1.0f),
                speed = new FsmFloat(8f),
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false
            });

            // 5. 移动到高处Y位置（上移5单位）
            actions.Add(new AnimateYPositionTo
            {
                GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                ToValue = new FsmFloat(147.5f),    // 高处Y（上移5单位）
                localSpace = false,
                time = new FsmFloat(1.0f),
                speed = new FsmFloat(5f),
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false,
            });

            // 6. 启动BOSS Z轴和Scale的渐变协程（同步进行）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartBossTransformAnimation") { Value = "StartBossTransformAnimation" },
                parameters = new FsmVar[0]
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(1.2f),
                finishEvent = FsmEvent.Finished
            });
            moveToCenterState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加生成大丝球状态的动作（实际上是启动蓄力，因为大丝球已在Prepare状态生成）
        /// </summary>
        private void AddBigSilkBallSpawnActions(FsmState spawnState)
        {
            var actions = new List<FsmStateAction>();

            // 启动大丝球蓄力（大丝球已在Prepare状态生成）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartBigSilkBallCharge") { Value = "StartBigSilkBallCharge" },
                parameters = new FsmVar[0]
            });

            // 启动等待大丝球完成的协程（异步）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("WaitForBigSilkBallComplete") { Value = "WaitForBigSilkBallComplete" },
                parameters = new FsmVar[0]
            });

            // 等待0.5秒后进入等待状态
            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            });

            spawnState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加等待状态的动作
        /// </summary>
        private void AddBigSilkBallWaitActions(FsmState waitState)
        {
            // Wait状态不需要任何Action，纯粹等待协程发送"BIG SILK BALL COMPLETE"事件
            // 协程已在Spawn状态启动，会在大丝球完成后发送自定义事件
            // 空状态不会触发FINISHED，只会等待Transition中定义的自定义事件
            waitState.Actions = new FsmStateAction[0];
        }

        /// <summary>
        /// 添加结束状态的动作（只做必要的清理）
        /// </summary>
        private void AddBigSilkBallEndActions(FsmState endState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 恢复Boss的Layer（从Invincible恢复到原始图层）

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("RestoreBossTransform") { Value = "RestoreBossTransform" },
                parameters = new FsmVar[0]
            });
            // --- 在结尾前加注入 ---
            actions.Insert(actions.Count, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SendBladesReturnToAllFingerBlades") { Value = "SendBladesReturnToAllFingerBlades" },
                parameters = new FsmVar[] { }
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished
            });

            endState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加返回状态的动作（3秒恢复期：前2秒BOSS返回前景，再等1秒）
        /// </summary>
        private void AddBigSilkBallReturnActions(FsmState returnState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = LayerMask.NameToLayer("Enemies")  // 恢复到敌人层
            });
            // 恢复haze子物品
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("EnableBossHaze") { Value = "EnableBossHaze" },
                parameters = new FsmVar[0]
            });
            // 2. 等待2秒（BOSS返回前景的时间）+ 额外1秒缓冲
            actions.Add(new Wait
            {
                time = new FsmFloat(1f),
                finishEvent = FsmEvent.Finished
            });

            // 3. 解锁Boss（发送BIG SILK BALL UNLOCK到Boss Control FSM）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("BIG SILK BALL UNLOCK") { Value = "BIG SILK BALL UNLOCK" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 4. 恢复攻击（发送ATTACK START到Attack Control FSM）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
                },
                sendEvent = new FsmString("ATTACK START") { Value = "ATTACK START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            returnState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加怒吼状态的动作
        /// </summary>
        private void AddBigSilkBallRoarActions(FsmState roarState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 播放Tk2d动画 "Roar"
            actions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Roar") { Value = "Roar" },
                animationTriggerEvent = FsmEvent.Finished
            });

            // 2. 直接克隆怒吼音效动作（从Attack Control FSM的Roar状态）
            var playRoarAudio = _attackControlBehavior?.CloneActionFromAttackControlFSM<PlayAudioEventRandom>("Roar");
            if (playRoarAudio != null)
            {
                actions.Add(playRoarAudio);
            }
            else
            {
                Log.Warn("无法克隆PlayAudioEventRandom动作，使用默认配置");
            }

            // 4. 发送事件
            if (_hairTransform != null)
            {
                actions.Add(new SendEventByName
                {
                    eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObject,
                        gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = _hairTransform.gameObject } },
                    },
                    sendEvent = new FsmString("ROAR") { Value = "ROAR" },
                    delay = new FsmFloat(0f),
                    everyFrame = false
                });
            }

            roarState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 延迟添加 Big Silk Ball Roar 状态的动作（等待 AttackControlBehavior 初始化）
        /// </summary>
        private IEnumerator DelayedAddBigSilkBallRoarActions(FsmState roarState)
        {
            // 等待1秒，确保 AttackControlBehavior 已初始化
            yield return new WaitForSeconds(1f);

            // 确保 AttackControlBehavior 已获取
            if (_attackControlBehavior == null)
            {
                _attackControlBehavior = gameObject.GetComponent<MemoryAttackControlBehavior>();
            }

            // 如果仍然为 null，继续等待
            int retryCount = 0;
            while (_attackControlBehavior == null && retryCount < 10)
            {
                yield return new WaitForSeconds(0.1f);
                _attackControlBehavior = gameObject.GetComponent<MemoryAttackControlBehavior>();
                retryCount++;
            }

            if (_attackControlBehavior == null)
            {
                Log.Error("延迟1秒后仍无法获取 AttackControlBehavior，Big Silk Ball Roar 状态可能缺少音效动作");
                yield break;
            }

            // 现在安全地添加动作
            Log.Info("延迟添加 Big Silk Ball Roar 状态的动作");
            AddBigSilkBallRoarActions(roarState);
        }

        /// <summary>
        /// 添加怒吼后移动到中心状态的动作
        /// </summary>
        private void AddBigSilkBallRoarEndActions(FsmState roarEndState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 设置速度为0（防止重力影响）
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new Vector2(0f, 0f),
                x = new FsmFloat { UseVariable = false },
                y = new FsmFloat(0f),
                everyFrame = false
            });

            actions.Add(new SendEventToRegister
            {
                eventName = new FsmString("ATTACK CLEAR") { Value = "ATTACK CLEAR" },
            });

            actions.Add(new Tk2dWatchAnimationEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animationCompleteEvent = FsmEvent.Finished,
                animationTriggerEvent = FsmEvent.Finished,
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            });
            roarEndState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加大招状态之间的转换
        /// 流程: HP Check 2.5 -> Roar -> Roar End -> Prepare -> Move To Center -> Spawn -> Wait -> End -> Return -> P3
        /// </summary>
        private void AddBigSilkBallTransitions(FsmState prepareState, FsmState moveToCenterState,
            FsmState spawnState, FsmState waitState, FsmState endState, FsmState returnState, FsmState p3State,
            FsmState roarState, FsmState roarEndState)
        {
            // 1. Roar -> Roar End
            SetFinishedTransition(roarState, roarEndState);

            // 2. Roar End -> Prepare
            SetFinishedTransition(roarEndState, prepareState);

            // 3. Prepare -> Move To Center
            SetFinishedTransition(prepareState, moveToCenterState);

            // 4. Move To Center -> Spawn
            SetFinishedTransition(moveToCenterState, spawnState);

            // 5. Spawn -> Wait
            SetFinishedTransition(spawnState, waitState);

            // 6. Wait -> End (监听自定义事件BIG SILK BALL COMPLETE)
            waitState.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("BIG SILK BALL COMPLETE"), endState)
            };

            // 7. End -> Return
            SetFinishedTransition(endState, returnState);

            // 8. Return -> P3
            SetFinishedTransition(returnState, p3State);

            Log.Info("已设置大招状态转换: Roar -> Roar End -> Prepare -> Move To Center -> Spawn -> Wait -> End -> Return -> P3");
        }

        /// <summary>
        /// 注册大招相关的新事件
        /// </summary>
        private void RegisterBigSilkBallEvents()
        {
            // 使用 FsmStateBuilder 批量注册事件
            RegisterEvents(_phaseControl,
                "START BIG SILK BALL",
                "BIG SILK BALL COMPLETE",
                "BIG SILK BALL LOCK",
                "BIG SILK BALL UNLOCK"
            );

            Log.Info("大招事件注册完成（START, COMPLETE, LOCK, UNLOCK）");
        }

        /// <summary>
        /// 向所有六根针发送事件，参数为object，提升PlayMaker CallMethod兼容性
        /// </summary>
        public void SendEventToAllFingerBlades(object eventNameObj)
        {
            string eventName = eventNameObj?.ToString() ?? "";
            Log.Info($"接收到参数: {eventNameObj}, 转换为字符串: {eventName}");
            foreach (var bladeObj in _allFingerBlades)
            {
                if (bladeObj != null)
                {
                    var fsm = bladeObj.GetComponent<PlayMakerFSM>();
                    if (fsm != null) { fsm.SendEvent(eventName); Log.Info($"向{bladeObj.name}发送:{eventName}"); }
                }
            }
        }

        /// <summary>
        /// 向所有六根针发送SILK STUNNED事件（供FSM CallMethod无参数调用）
        /// </summary>
        public void SendSilkStunnedToAllFingerBlades()
        {
            SendEventToAllFingerBlades("SILK STUNNED");
        }

        /// <summary>
        /// 向所有六根针发送BLADES RETURN事件（供FSM CallMethod无参数调用）
        /// </summary>
        public void SendBladesReturnToAllFingerBlades()
        {
            SendEventToAllFingerBlades("BLADES RETURN");
        }
        /// <summary>
        /// 向所有六根针发送BLADES RETURN事件（供FSM CallMethod无参数调用）
        /// </summary>
        public void SendBladesReturnDelay()
        {
            StartCoroutine(SendBladesReturnDelayCoroutine());
        }
        private IEnumerator SendBladesReturnDelayCoroutine()
        {
            // ⚠️ 关键修复：发送两次BLADES RETURN，因为指针有两个状态需要此事件
            // 
            // 第一次：延迟3.5秒，确保指针走完Stagger流程到达Stagger Finish状态
            // 指针Stagger流程：Stagger Pause(0-0.3s) → Stagger Anim(0.4-1s) → Stagger Drop(2.5s) → Stagger Finish
            // Stagger Finish状态接收BLADES RETURN → 进入Rerise Follow
            yield return new WaitForSeconds(3.5f);
            SendBladesReturnToAllFingerBlades();
            Log.Info("第一次发送 BLADES RETURN（针对Stagger Finish状态）");

            // 第二次：再延迟1秒，针对可能处于Rise状态的指针
            // Rise状态接收BLADES RETURN → 进入Begin
            // 模拟原版Rerise Up状态的第二次发送
            yield return new WaitForSeconds(1f);
            SendBladesReturnToAllFingerBlades();
            Log.Info("第二次发送 BLADES RETURN（针对Rise状态）");
        }

        /// <summary>
        /// 在爬升阶段完成时重置Finger Blade状态
        /// 这是为了弥补跳过Move Stop导致的事件丢失
        /// </summary>
        public void ResetFingerBladesOnClimbComplete()
        {
            // ⚠️ 注意：不在这里立即发送BLADES RETURN
            // 因为SendBladesReturnDelay已经在3.5秒后发送过了
            // 如果此时指针还在Stagger流程中，立即发送会导致事件丢失
            // 
            // 只重置Finger Blade的状态标志，确保它们处于正确的初始状态
            foreach (var bladeObj in _allFingerBlades)
            {
                if (bladeObj != null)
                {
                    var fsm = bladeObj.GetComponent<PlayMakerFSM>();
                    if (fsm != null)
                    {
                        // 重置Ready标志
                        var readyVar = fsm.FsmVariables.GetFsmBool("Ready");
                        if (readyVar != null)
                        {
                            readyVar.Value = false;
                        }

                        Log.Info($"重置Finger Blade {bladeObj.name} Ready标志");
                    }
                }
            }
        }
        /// <summary>
        /// 保存Boss原始图层
        /// </summary>
        public void SaveOriginalLayer()
        {
            _originalLayer = gameObject.layer;
            Log.Info($"保存Boss原始图层: {_originalLayer}");
        }

        /// <summary>
        /// 恢复Boss原始图层
        /// </summary>
        public void RestoreOriginalLayer()
        {
            gameObject.layer = _originalLayer;
            Log.Info($"恢复Boss图层: {_originalLayer}");
        }

        /// <summary>
        /// 生成大丝球（不启动蓄力）
        /// </summary>
        public void SpawnBigSilkBall()
        {
            if (_bigSilkBallManager == null)
            {
                Log.Error("BigSilkBallManager 未初始化，无法生成大丝球");
                return;
            }

            if (_bigSilkBallTriggered)
            {
                Log.Warn("大丝球已经触发过，跳过生成");
                return;
            }

            _bigSilkBallTriggered = true;

            // 在Boss位置生成大丝球
            Vector3 bossPosition = gameObject.transform.position;
            _currentBigSilkBall = _bigSilkBallManager.SpawnMemoryBigSilkBall(bossPosition, gameObject);

            if (_currentBigSilkBall != null)
            {
                Log.Info($"大丝球生成成功，位置: {bossPosition}");
            }
            else
            {
                Log.Error("大丝球生成失败");
            }
        }

        /// <summary>
        /// 启动大丝球蓄力
        /// </summary>
        public void StartBigSilkBallCharge()
        {
            if (_currentBigSilkBall == null)
            {
                Log.Error("大丝球不存在，无法启动蓄力");
                return;
            }

            var behavior = _currentBigSilkBall.GetComponent<MemoryBigSilkBallBehavior>();
            if (behavior != null)
            {
                Log.Info("启动大丝球蓄力");
                behavior.StartCharge();
            }
            else
            {
                Log.Error("大丝球缺少 MemoryBigSilkBallBehavior 组件");
            }
        }

        /// <summary>
        /// 延迟启动蓄力
        /// </summary>
        private IEnumerator DelayedStartCharge()
        {
            yield return new WaitForSeconds(0.5f);

            if (_currentBigSilkBall != null)
            {
                var behavior = _currentBigSilkBall.GetComponent<MemoryBigSilkBallBehavior>();
                if (behavior != null)
                {
                    behavior.StartCharge();
                    Log.Info("大丝球开始蓄力");
                }
            }
        }

        /// <summary>
        /// 接收来自BigSilkBallBehavior的事件通知
        /// </summary>
        public void OnBigSilkBallEvent(string eventName)
        {
            Log.Info($"收到大丝球事件: {eventName}");

            switch (eventName)
            {
                case "ChargeComplete":
                    _chargeComplete = true;
                    Log.Info("大丝球蓄力完成");
                    break;

                case "BurstComplete":
                    _burstComplete = true;
                    Log.Info("大丝球爆炸完成");
                    break;

                default:
                    Log.Warn($"未知的大丝球事件: {eventName}");
                    break;
            }
        }

        /// <summary>
        /// 等待大丝球完成（协程）
        /// </summary>
        public void WaitForBigSilkBallComplete()
        {
            StartCoroutine(WaitForBigSilkBallCompleteCoroutine());
        }

        /// <summary>
        /// 等待大丝球完成的协程
        /// </summary>
        private IEnumerator WaitForBigSilkBallCompleteCoroutine()
        {
            Log.Info("开始等待大丝球完成...");

            // 重置事件标记
            _chargeComplete = false;
            _burstComplete = false;

            // 等待蓄力完成
            float waitTime = 0f;
            while (!_chargeComplete)
            {
                waitTime += Time.deltaTime;
                if (waitTime > 5f)  // 超时保护：5秒
                {
                    Log.Warn("等待蓄力完成超时（5秒），强制继续");
                    break;
                }
                yield return null;
            }
            Log.Info($"确认蓄力完成（等待时间: {waitTime:F2}s），等待爆炸...");

            // 等待爆炸完成
            waitTime = 0f;
            while (!_burstComplete)
            {
                waitTime += Time.deltaTime;
                if (waitTime > 15f)  // 超时保护：15秒（动画11.53秒+缓冲）
                {
                    Log.Warn("等待爆炸完成超时（15秒），强制继续");
                    break;
                }
                yield return null;
            }
            Log.Info($"确认爆炸完成（等待时间: {waitTime:F2}s），大招结束");

            // 发送自定义完成事件到FSM
            if (_phaseControl != null)
            {
                _phaseControl.SendEvent("BIG SILK BALL COMPLETE");
                Log.Info("已发送 BIG SILK BALL COMPLETE 事件到 Phase Control FSM");
            }
        }


        /// <summary>
        /// 保存BOSS原始状态（Z轴和Scale），同时保存Hair的状态
        /// </summary>
        public void SaveBossOriginalState()
        {
            // 保存BOSS状态
            _originalBossZ = transform.position.z;
            _originalBossScale = transform.localScale;
            Log.Info($"保存BOSS原始状态 - Z轴: {_originalBossZ:F4}, Scale: {_originalBossScale}");

            // 查找并保存Hair状态（Hair是BOSS的兄弟物体，在同一父级下）
            if (_hairTransform == null && transform.parent != null)
            {
                _hairTransform = transform.parent.Find("Silk_Hair");
                if (_hairTransform == null)
                {
                    Log.Warn("未找到 Silk_Hair 物体（可能名称不同或不存在）");
                }
            }

            if (_hairTransform != null)
            {
                _originalHairZ = _hairTransform.position.z;
                _originalHairScale = _hairTransform.localScale;
                Log.Info($"保存Hair原始状态 - Z轴: {_originalHairZ:F4}, Scale: {_originalHairScale}");
            }
        }

        /// <summary>
        /// 启动BOSS Transform渐变动画（Z轴和Scale）
        /// </summary>
        public void StartBossTransformAnimation()
        {
            StartCoroutine(AnimateBossTransform());
        }

        /// <summary>
        /// BOSS Transform渐变协程（Z轴移到后面，Scale放大补偿），同时调整Hair
        /// </summary>
        private IEnumerator AnimateBossTransform()
        {
            float duration = 1.0f;  // 渐变时长1秒，与XY位置移动同步
            float targetZ = 60f;  // 目标Z轴（调整到更深的背景）
            float targetScale = 1.8f;   // 目标Scale倍数（补偿Z轴后移）

            Log.Info($"开始BOSS Transform渐变 - 从Z={_originalBossZ:F4}到{targetZ:F4}, Scale从{_originalBossScale}到{_originalBossScale * targetScale}");

            float elapsed = 0f;
            Vector3 bossStartScale = _originalBossScale.Abs();
            transform.localScale = bossStartScale;
            Vector3 bossEndScale = _originalBossScale.Abs() * targetScale;

            // Hair的渐变参数
            Vector3 hairStartScale = _originalHairScale;
            Vector3 hairEndScale = _originalHairScale * targetScale;

            if (_hairTransform != null)
            {
                Log.Info($"开始Hair Transform渐变 - 从Z={_originalHairZ:F4}到{targetZ:F4}, Scale从{_originalHairScale}到{hairEndScale}");
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Lerp BOSS Z轴和Scale
                Vector3 bossPos = transform.position;
                bossPos.z = Mathf.Lerp(_originalBossZ, targetZ, t);
                transform.position = bossPos;
                transform.localScale = Vector3.Lerp(bossStartScale, bossEndScale, t);

                // Lerp Hair Z轴和Scale
                if (_hairTransform != null)
                {
                    Vector3 hairPos = _hairTransform.position;
                    hairPos.z = Mathf.Lerp(_originalHairZ, targetZ, t);
                    _hairTransform.position = hairPos;
                    _hairTransform.localScale = Vector3.Lerp(hairStartScale, hairEndScale, t);
                }

                yield return null;
            }

            // 确保最终值精确
            Vector3 bossFinalPos = transform.position;
            bossFinalPos.z = targetZ;
            transform.position = bossFinalPos;
            transform.localScale = bossEndScale;

            if (_hairTransform != null)
            {
                Vector3 hairFinalPos = _hairTransform.position;
                hairFinalPos.z = targetZ;
                _hairTransform.position = hairFinalPos;
                _hairTransform.localScale = hairEndScale;
                Log.Info($"Hair Transform渐变完成 - Z={_hairTransform.position.z:F4}, Scale={_hairTransform.localScale}");
            }

            Log.Info($"BOSS Transform渐变完成 - Z={transform.position.z:F4}, Scale={transform.localScale}");
        }

        /// <summary>
        /// 恢复BOSS原始Transform（大招结束后调用）
        /// </summary>
        public void RestoreBossTransform()
        {
            StartCoroutine(RestoreBossTransformCoroutine());
        }

        /// <summary>
        /// 禁用Boss的haze子物品（大招期间）
        /// </summary>
        public void DisableBossHaze()
        {
            if (_bossHaze != null)
            {
                _bossHaze.SetActive(false);
                Log.Info("已禁用 haze2 (7)");
            }
            else
            {
                Log.Warn("_bossHaze 为 null，无法禁用");
            }

            if (_bossHaze2 != null)
            {
                _bossHaze2.SetActive(false);
                Log.Info("已禁用 haze2 (8)");
            }
            else
            {
                Log.Warn("_bossHaze2 为 null，无法禁用");
            }
        }

        /// <summary>
        /// 恢复Boss的haze子物品（大招结束后）
        /// </summary>
        public void EnableBossHaze()
        {
            if (_bossHaze != null)
            {
                _bossHaze.SetActive(true);
                Log.Info("已恢复 haze2 (7)");
            }
            else
            {
                Log.Warn("_bossHaze 为 null，无法恢复");
            }

            if (_bossHaze2 != null)
            {
                _bossHaze2.SetActive(true);
                Log.Info("已恢复 haze2 (8)");
            }
            else
            {
                Log.Warn("_bossHaze2 为 null，无法恢复");
            }
        }

        /// <summary>
        /// 恢复BOSS Transform的协程（渐变过程），同时恢复Hair
        /// </summary>
        private IEnumerator RestoreBossTransformCoroutine()
        {
            float duration = 2.0f;  // 恢复时间（2秒，让玩家看清BOSS从背景返回）

            // BOSS当前状态
            Vector3 bossCurrentPos = transform.position;
            Vector3 bossCurrentScale = transform.localScale;
            float bossStartZ = bossCurrentPos.z;

            Log.Info($"开始恢复BOSS Transform - 从Z={bossCurrentPos.z:F4}到{_originalBossZ:F4}, Scale从{bossCurrentScale}到(1,1,1)");

            // Hair当前状态
            Vector3 hairCurrentPos = Vector3.zero;
            Vector3 hairCurrentScale = Vector3.one;
            float hairStartZ = 0f;

            if (_hairTransform != null)
            {
                hairCurrentPos = _hairTransform.position;
                hairCurrentScale = _hairTransform.localScale;
                hairStartZ = hairCurrentPos.z;
                Log.Info($"开始恢复Hair Transform - 从Z={hairCurrentPos.z:F4}到{_originalHairZ:F4}, Scale从{hairCurrentScale}到(1,1,1)");
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Lerp BOSS Z轴和Scale（Scale恢复到(1,1,1)）
                Vector3 bossPos = transform.position;
                bossPos.z = Mathf.Lerp(bossStartZ, _originalBossZ, t);
                transform.position = bossPos;
                transform.localScale = Vector3.Lerp(bossCurrentScale, Vector3.one, t);

                // Lerp Hair Z轴和Scale（Scale恢复到(1,1,1)）
                if (_hairTransform != null)
                {
                    Vector3 hairPos = _hairTransform.position;
                    hairPos.z = Mathf.Lerp(hairStartZ, _originalHairZ, t);
                    _hairTransform.position = hairPos;
                    _hairTransform.localScale = Vector3.Lerp(hairCurrentScale, Vector3.one, t);
                }

                yield return null;
            }

            // 确保最终值精确
            Vector3 bossFinalPos = transform.position;
            bossFinalPos.z = _originalBossZ;
            transform.position = bossFinalPos;
            transform.localScale = Vector3.one;  // 强制恢复为(1,1,1)

            if (_hairTransform != null)
            {
                Vector3 hairFinalPos = _hairTransform.position;
                hairFinalPos.z = _originalHairZ;
                _hairTransform.position = hairFinalPos;
                _hairTransform.localScale = Vector3.one;  // 强制恢复为(1,1,1)
                Log.Info($"Hair Transform恢复完成 - Z={_hairTransform.position.z:F4}, Scale={_hairTransform.localScale}");
            }

            Log.Info($"BOSS Transform恢复完成 - Z={transform.position.z:F4}, Scale={transform.localScale}");
        }
        #endregion
    }
}