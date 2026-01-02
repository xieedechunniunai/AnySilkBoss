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
    internal partial class PhaseControlBehavior : MonoBehaviour
    {
        #region 爬升阶段相关
        /// <summary>
        /// 获取 Boss Control FSM 引用
        /// </summary>
        private void GetBossControlFSM()
        {
            _bossControl = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_bossControl == null)
            {
                Log.Error("未找到 Boss Control FSM");
            }
            else
            {
                Log.Info("成功获取 Boss Control FSM");
            }
        }

        /// <summary>
        /// 添加爬升阶段状态序列
        /// </summary>
        private void AddClimbPhaseStates()
        {
            if (_phaseControl == null)
            {
                Log.Error("Phase Control FSM 未初始化，无法添加爬升阶段");
                return;
            }

            Log.Info("=== 开始添加爬升阶段状态序列 ===");

            // 找到关键状态
            var staggerPauseState = FindState(_phaseControl, "Stagger Pause");
            var setP4State = FindState(_phaseControl, "Set P4");

            if (staggerPauseState == null)
            {
                Log.Error("未找到 Stagger Pause 状态");
                return;
            }
            if (setP4State == null)
            {
                Log.Error("未找到 Set P4 状态");
                return;
            }

            // 注册新事件
            RegisterClimbPhaseEvents();

            // 创建新变量
            CreateClimbPhaseVariables();

            // 批量创建爬升阶段状态
            var climbStates = CreateStates(_phaseControl.Fsm,
                ("Climb Init Catch", "硬控玩家并移动到地面"),
                ("Climb Wait Roar", "等待Boss吼叫完成"),
                ("Climb Silk Activate", "激活丝线缠绕"),
                ("Climb Catch Effect", "播放音频、隐藏玩家、激活替身"),
                ("Climb Player Prepare", "恢复玩家重力和显示"),
                ("Climb Phase Player Control", "玩家动画控制"),
                ("Climb Phase Boss Active", "Boss漫游+玩家进度监控"),
                ("Climb Phase Complete", "爬升阶段完成")
            );

            // 解构状态引用
            var climbInitCatchState = climbStates[0];
            var climbWaitRoarState = climbStates[1];
            var climbSilkActivateState = climbStates[2];
            var climbCatchEffectState = climbStates[3];
            var climbPlayerPrepareState = climbStates[4];
            var climbPlayerControlState = climbStates[5];
            var climbBossActiveState = climbStates[6];
            var climbCompleteState = climbStates[7];

            // 添加状态到FSM
            AddStatesToFsm(_phaseControl, climbStates);

            // 修改 Stagger Pause 的跳转（跳到新的初始状态）
            ModifyStaggerPauseTransition(staggerPauseState, climbInitCatchState);

            // 添加状态动作
            AddClimbInitCatchActions(climbInitCatchState);
            AddClimbWaitRoarActions(climbWaitRoarState);
            AddClimbSilkActivateActions(climbSilkActivateState);
            AddClimbCatchEffectActions(climbCatchEffectState);
            AddClimbPlayerPrepareActions(climbPlayerPrepareState);
            AddClimbPhasePlayerControlActions(climbPlayerControlState);
            AddClimbPhaseBossActiveActions(climbBossActiveState);
            AddClimbPhaseCompleteActions(climbCompleteState);

            // 添加状态转换（新流程）
            AddClimbPhaseTransitionsNew(climbInitCatchState, climbWaitRoarState,
                climbSilkActivateState, climbCatchEffectState, climbPlayerPrepareState, climbPlayerControlState,
                climbBossActiveState, climbCompleteState, setP4State);

            // 重新初始化FSM
            // ReinitializeFsm(_phaseControl);

            Log.Info("=== 爬升阶段状态序列添加完成 ===");
        }

        /// <summary>
        /// 注册爬升阶段事件
        /// </summary>
        private void RegisterClimbPhaseEvents()
        {
            // 使用 FsmStateBuilder 批量注册事件
            RegisterEvents(_phaseControl,
                "CLIMB PHASE START",
                "CLIMB PHASE END",
                "CLIMB PHASE ATTACK",
                "CLIMB COMPLETE",
                "CLIMB ROAR START",
                "CLIMB ROAR DONE"
            );
            Log.Info("爬升阶段事件注册完成（含Roar协同事件）");
        }

        /// <summary>
        /// 创建爬升阶段变量
        /// </summary>
        private void CreateClimbPhaseVariables()
        {
            var boolVars = _phaseControl.FsmVariables.BoolVariables.ToList();

            // 检查是否已存在
            if (!boolVars.Any(v => v.Name == "Climb Phase Active"))
            {
                var climbPhaseActive = new FsmBool("Climb Phase Active") { Value = false };
                boolVars.Add(climbPhaseActive);
                _phaseControl.FsmVariables.BoolVariables = boolVars.ToArray();
                Log.Info("创建 Climb Phase Active 变量");
            }

            // ⚠️ 创建Phase2特殊攻击变量
            if (!boolVars.Any(v => v.Name == "Special Attack"))
            {
                var specialAttack = new FsmBool("Special Attack") { Value = false };
                boolVars.Add(specialAttack);
                _phaseControl.FsmVariables.BoolVariables = boolVars.ToArray();
                Log.Info("创建 Special Attack 变量（用于Phase2特殊攻击）");
            }
        }

        /// <summary>
        /// 修改 Stagger Pause 的跳转
        /// </summary>
        private void ModifyStaggerPauseTransition(FsmState staggerPauseState, FsmState climbInitState)
        {
            // 找到所有跳转到 BG Break Sequence 的转换，改为跳转到 Climb Init Catch
            foreach (var transition in staggerPauseState.Transitions)
            {
                if (transition.toState == "BG Break Sequence")
                {
                    transition.toState = "Climb Init Catch";
                    transition.toFsmState = climbInitState;
                    Log.Info("已修改 Stagger Pause -> Climb Init Catch");
                }
            }
        }

        /// <summary>
        /// 添加 Climb Init Catch 动作（发送ROAR事件，让原版Roar机制处理玩家硬控）
        /// </summary>
        private void AddClimbInitCatchActions(FsmState initState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 停止所有攻击
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

            // 2. 发送 CLIMB ROAR START 给 Boss Control（全局转换，会中断Boss当前行为）
            // Boss Control 的 Climb Roar 状态会使用 StartRoarEmitter (stunHero=true) 来硬控玩家
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("CLIMB ROAR START") { Value = "CLIMB ROAR START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 3. 设置Boss无敌
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = 2  // Invincible
            });

            // 4. 设置阶段标志
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Phase Control") { Value = "Phase Control" },
                variableName = new FsmString("Climb Phase Active") { Value = "Climb Phase Active" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            // 5. 等待一小段时间让 Roar 事件传递
            actions.Add(new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            });

            initState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Wait Roar 动作（等待Boss Roar完成）
        /// </summary>
        private void AddClimbWaitRoarActions(FsmState waitState)
        {
            // 此状态播放玩家 Roar Lock 动画，等待 CLIMB ROAR DONE 事件
            var actions = new List<FsmStateAction>();
            if (_fsmHero != null)
            {
                actions.Add(new Tk2dPlayAnimation
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmHero
                    },
                    animLibName = new FsmString("") { Value = "" },
                    clipName = new FsmString("Roar Lock") { Value = "Roar Lock" },
                });
            }
            else
            {
                Log.Warn("AddClimbWaitRoarActions: _fsmHero 为空，跳过玩家动画");
            }
            waitState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Silk Activate 动作（激活丝线）
        /// 使用原生FSM Action: GetPosition + SetPosition + SetRotation + ActivateGameObject
        /// 此时 Roar 已结束，需要手动禁用玩家输入
        /// </summary>
        private void AddClimbSilkActivateActions(FsmState silkState)
        {
            var actions = new List<FsmStateAction>();

            if (_fsmHero == null || _fsmHeroX == null || _fsmHeroY == null)
            {
                Log.Error("FSM变量未初始化，无法添加丝线激活动作");
                silkState.Actions = new FsmStateAction[0];
                return;
            }

            // [0] Roar 结束后，手动禁用玩家输入（简单的输入禁用，不需要处理复杂的 Roar 状态）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DisablePlayerInputAfterRoar") { Value = "DisablePlayerInputAfterRoar" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            float offsetDistance = 12.5f;

            // 五个位置的偏移量：上、左上、右上、左下、右下
            float[] offsetsX = { 0f, -offsetDistance, offsetDistance, -offsetDistance, offsetDistance };
            float[] offsetsY = { offsetDistance, offsetDistance, offsetDistance, -offsetDistance, -offsetDistance };
            // 五个旋转角度（180°是垂直向下，统一朝向玩家中心）
            float[] rotations = { 180f, 45f, -45f, 135f, -135f };
            //float[] rotations = { -90f, -45f, -135f, 45f, 135f };
            // [0] 获取Hero位置
            actions.Add(new GetPosition
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    GameObject = _fsmHero
                },
                vector = new FsmVector3(),
                x = _fsmHeroX,
                y = _fsmHeroY,
                z = new FsmFloat(),
                space = Space.World,
                everyFrame = false
            });

            // 为每个丝线克隆体添加: SetPosition + SetRotation + ActivateGameObject
            for (int i = 0; i < 5; i++)
            {
                if (_fsmSilkYankClones[i] == null) continue;

                // 创建临时变量存储计算后的位置
                var posX = new FsmFloat($"SilkYank_PosX_{i}");
                var posY = new FsmFloat($"SilkYank_PosY_{i}");

                // 计算X位置: HeroX + offsetX
                actions.Add(new FloatOperator
                {
                    float1 = _fsmHeroX,
                    float2 = new FsmFloat(offsetsX[i]),
                    operation = FloatOperator.Operation.Add,
                    storeResult = posX,
                    everyFrame = false
                });

                // 计算Y位置: HeroY + offsetY
                actions.Add(new FloatOperator
                {
                    float1 = _fsmHeroY,
                    float2 = new FsmFloat(offsetsY[i]),
                    operation = FloatOperator.Operation.Add,
                    storeResult = posY,
                    everyFrame = false
                });

                // SetPosition
                actions.Add(new SetPosition
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmSilkYankClones[i]
                    },
                    vector = new FsmVector3(),
                    x = posX,
                    y = posY,
                    z = new FsmFloat(0f),
                    space = Space.World,
                    everyFrame = false
                });

                // SetRotation
                actions.Add(new SetRotation
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmSilkYankClones[i]
                    },
                    quaternion = new FsmQuaternion(),
                    vector = new FsmVector3(),
                    xAngle = new FsmFloat(0f),
                    yAngle = new FsmFloat(0f),
                    zAngle = new FsmFloat(rotations[i]),
                    space = Space.World,
                    everyFrame = false
                });

                // ActivateGameObject
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmSilkYankClones[i]
                    },
                    activate = new FsmBool(true),
                    recursive = new FsmBool(false),
                    resetOnExit = false,
                    everyFrame = false
                });
            }

            // 等待0.3秒（丝线生成时间）
            actions.Add(new Wait
            {
                time = new FsmFloat(0.3f),
                finishEvent = FsmEvent.Finished
            });

            silkState.Actions = actions.ToArray();
            Log.Info("丝线激活动作已添加（使用FSM原生Action）");
        }

        /// <summary>
        /// 添加 Climb Catch Effect 动作（音频+隐藏玩家+激活替身）
        /// </summary>
        private void AddClimbCatchEffectActions(FsmState catchEffectState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 获取玩家位置并设置替身位置
            if (_fsmHero != null && _fsmWebStrandCatchEffect != null)
            {
                // 设置替身位置到玩家位置
                actions.Add(new SetPosition
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmWebStrandCatchEffect,
                        gameObject = _webStrandCatchEffect
                    },
                    vector = new FsmVector3(),
                    x = _fsmHeroX ?? new FsmFloat { UseVariable = false, Value = 0f },
                    y = _fsmHeroY ?? new FsmFloat { UseVariable = false, Value = 0f },
                    z = new FsmFloat { UseVariable = false, Value = 0.006f },
                    space = Space.World,
                    everyFrame = false
                });
                // 3. 隐藏玩家（使用 SetMeshRendererEnabled）
                actions.Add(new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmHero
                    },
                    active = false
                });
                actions.Add(new MatchScaleSign
                {
                    Target = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmWebStrandCatchEffect
                    },
                    MatchTo = _fsmHero,
                    active = false
                });
                // 2. 激活替身
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = _fsmWebStrandCatchEffect,
                        GameObject = _webStrandCatchEffect
                    },
                    activate = new FsmBool(true) { Value = true },
                    recursive = new FsmBool(false) { Value = false },
                    resetOnExit = false,
                    everyFrame = false
                });
            }
            else
            {
                Log.Warn("Climb Catch Effect: _fsmHero 或 _fsmWebStrandCatchEffect 为 null，跳过视觉效果设置");
            }

            // 4. 等待1.5秒（原始Catch到恢复的时间）
            actions.Add(new Wait
            {
                time = new FsmFloat(1.5f),
                finishEvent = FsmEvent.Finished
            });

            catchEffectState.Actions = actions.ToArray();

            // 延迟添加音频动作（等待 AttackControlBehavior 初始化）
            StartCoroutine(DelayedAddClimbCatchAudioActions(catchEffectState));
        }

        /// <summary>
        /// 延迟添加爬升阶段 Catch 音频动作（等待 AttackControlBehavior 初始化）
        /// </summary>
        private IEnumerator DelayedAddClimbCatchAudioActions(FsmState catchEffectState)
        {
            // 获取 AttackControlBehavior
            if (_attackControlBehavior == null)
            {
                _attackControlBehavior = gameObject.GetComponent<AttackControlBehavior>();
            }

            // 等待 AttackControlBehavior 初始化完成
            if (_attackControlBehavior != null)
            {
                int waitCount = 0;
                while (!_attackControlBehavior.IsAttackControlFsmReady && waitCount < 50)
                {
                    yield return null;
                    waitCount++;
                }
            }

            if (_attackControlBehavior == null || !_attackControlBehavior.IsAttackControlFsmReady)
            {
                Log.Warn("无法获取 AttackControlBehavior 或其未初始化，Climb Catch Effect 音频动作未添加");
                yield break;
            }

            // 从 Catch 状态复制音频行为
            var audioActions = new List<FsmStateAction>();
            var audioAction1 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayRandomAudioClipTable>("Catch");
            var audioAction2 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 0);
            var audioAction3 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 1);
            var audioAction4 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 2);
            var audioAction5 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 3);

            if (audioAction1 != null) audioActions.Add(audioAction1);
            if (audioAction2 != null) audioActions.Add(audioAction2);
            if (audioAction3 != null) audioActions.Add(audioAction3);
            if (audioAction4 != null) audioActions.Add(audioAction4);
            if (audioAction5 != null) audioActions.Add(audioAction5);

            // 重新构建动作列表：视觉效果动作 + 音频 + Wait
            var newActions = new List<FsmStateAction>();
            // 添加除最后一个 Wait 外的所有原有动作
            for (int i = 0; i < catchEffectState.Actions.Length - 1; i++)
            {
                newActions.Add(catchEffectState.Actions[i]);
            }
            // 插入音频动作
            newActions.AddRange(audioActions);
            // 添加最后的 Wait
            newActions.Add(catchEffectState.Actions[catchEffectState.Actions.Length - 1]);

            catchEffectState.Actions = newActions.ToArray();
            catchEffectState.SaveActions();
            catchEffectState.LoadActions();
            Log.Info($"爬升阶段：Catch 音频动作已延迟添加（{audioActions.Count}个）");
        }

        /// <summary>
        /// 添加 Climb Player Prepare 动作（恢复重力/显示）
        /// </summary>
        private void AddClimbPlayerPrepareActions(FsmState prepareState)
        {
            var actions = new List<FsmStateAction>();

            // 恢复玩家重力和显示
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PreparePlayerForFall") { Value = "PreparePlayerForFall" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 等待0.2秒
            actions.Add(new Wait
            {
                time = new FsmFloat(0.2f),
                finishEvent = FsmEvent.Finished
            });

            prepareState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Phase Player Control 动作
        /// </summary>
        private void AddClimbPhasePlayerControlActions(FsmState playerControlState)
        {
            var actions = new List<FsmStateAction>();

            // 调用协程控制玩家动画
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartClimbPhasePlayerAnimation") { Value = "StartClimbPhasePlayerAnimation" },
                parameters = new FsmVar[0]
            });

            // 延后跳转，等待玩家稳定（避免玩家跳跃导致检测错误）
            actions.Add(new Wait
            {
                time = new FsmFloat(1.5f),
                finishEvent = FsmEvent.Finished
            });

            playerControlState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Phase Boss Active 动作
        /// </summary>
        private void AddClimbPhaseBossActiveActions(FsmState bossActiveState)
        {
            var actions = new List<FsmStateAction>();

            // 通知Boss Control进入漫游模式
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("CLIMB PHASE START") { Value = "CLIMB PHASE START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // ⚠️ 重要：保持原始FSM的指针复位逻辑
            // 在进入爬升阶段后1秒，发送BLADES RETURN让指针回位
            // 这是必要的，不能删除，否则指针可能永远收不到复位事件
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SendBladesReturnDelay") { Value = "SendBladesReturnDelay" },
                parameters = new FsmVar[0]
            });

            // 每帧监控玩家Y位置
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("MonitorPlayerClimbProgress") { Value = "MonitorPlayerClimbProgress" },
                parameters = new FsmVar[0],
                everyFrame = true
            });

            bossActiveState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Phase Complete 动作
        /// </summary>
        private void AddClimbPhaseCompleteActions(FsmState completeState)
        {
            var actions = new List<FsmStateAction>();

            // 恢复Boss正常图层
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = 11  // Enemies
            });

            // ⚠️ 恢复Boss的Z轴到0.01（原始值）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("RestoreBossZPosition") { Value = "RestoreBossZPosition" },
                parameters = new FsmVar[0]
            });

            // 清除阶段标志
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Phase Control") { Value = "Phase Control" },
                variableName = new FsmString("Climb Phase Active") { Value = "Climb Phase Active" },
                setValue = new FsmBool(false),
                everyFrame = false
            });

            // 通知Boss Control结束漫游
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("CLIMB PHASE END") { Value = "CLIMB PHASE END" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // ⚠️ 注意：Attack Control 的 CLIMB PHASE END 事件将在 Boss 完全返回场地后由协程发送
            // 这样可以确保 Boss 完全恢复后再开始攻击

            // 重置Finger Blade状态（弥补跳过Move Stop导致的BLADES RETURN事件）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ResetFingerBladesOnClimbComplete") { Value = "ResetFingerBladesOnClimbComplete" },
                parameters = new FsmVar[0]
            });

            // ⚠️ 快速移动Boss回到战斗场地（Y=50附近）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("MoveBossBackToArena") { Value = "MoveBossBackToArena" },
                parameters = new FsmVar[0]
            });

            // 重置C#端标志
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ResetClimbPhaseFlags") { Value = "ResetClimbPhaseFlags" },
                parameters = new FsmVar[0]
            });

            // ⚠️ 等待2.5秒：1.5秒Boss移动 + 1秒额外缓冲，确保Boss完全返回后再进入下一阶段
            actions.Add(new Wait
            {
                time = new FsmFloat(2.5f),
                finishEvent = FsmEvent.Finished
            });

            completeState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加爬升阶段转换（新流程）
        /// </summary>
        private void AddClimbPhaseTransitionsNew(
            FsmState initCatchState, FsmState waitRoarState,
            FsmState silkActivateState, FsmState catchEffectState, FsmState playerPrepareState,
            FsmState playerControlState, FsmState bossActiveState,
            FsmState completeState, FsmState setP4State)
        {
            // Init Catch -> Wait Roar (FINISHED)
            SetFinishedTransition(initCatchState, waitRoarState);

            // Wait Roar -> Silk Activate (CLIMB ROAR DONE)
            waitRoarState.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB ROAR DONE"), silkActivateState)
            };

            // Silk Activate -> Catch Effect (FINISHED)
            SetFinishedTransition(silkActivateState, catchEffectState);

            // Catch Effect -> Player Prepare (FINISHED)
            SetFinishedTransition(catchEffectState, playerPrepareState);

            // Player Prepare -> Player Control (FINISHED)
            SetFinishedTransition(playerPrepareState, playerControlState);

            // Player Control -> Boss Active (FINISHED)
            SetFinishedTransition(playerControlState, bossActiveState);

            // Boss Active -> Complete (CLIMB COMPLETE)
            bossActiveState.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB COMPLETE"), completeState)
            };

            // Complete -> Set P4 (FINISHED)
            SetFinishedTransition(completeState, setP4State);

            Log.Info("爬升阶段转换设置完成（新流程）");
        }

        #region 爬升阶段C#辅助方法

        /// <summary>
        /// Roar 结束后禁用玩家输入（简单的输入禁用，Roar 已处理完复杂状态）
        /// </summary>
        public void DisablePlayerInputAfterRoar()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("DisablePlayerInputAfterRoar: HeroController未找到");
                return;
            }

            var rb = hero.GetComponent<Rigidbody2D>();

            // Roar 结束后，玩家状态已被原版机制恢复，现在我们只需要简单禁用输入
            GameManager._instance?.inputHandler?.StopAcceptingInput();
            hero.RelinquishControl();
            hero.StopAnimationControl();
            hero.AffectedByGravity(false);

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            // 设置玩家无敌
            PlayerData.instance.isInvincible = true;

            Log.Info("Roar结束后禁用玩家输入完成，开始平滑移动到地面");

            // 平滑移动玩家到Y=133.57（保持X不变）
            StartCoroutine(AnimatePlayerToGround());
        }

        /// <summary>
        /// 硬控玩家并移动到地面（已废弃，保留兼容）
        /// </summary>
        [System.Obsolete("使用 DisablePlayerInputAfterRoar 替代，让原版 Roar 机制处理硬控")]
        public void CatchPlayerForClimb()
        {
            // 直接调用新方法
            DisablePlayerInputAfterRoar();
        }

        /// <summary>
        /// 设置捕捉效果（隐藏英雄 + 激活替身）
        /// </summary>
        public void SetupCatchEffect()
        {
            var hero = HeroController.instance;
            if (hero == null) return;

            // 隐藏英雄的MeshRenderer
            var heroRenderer = hero.GetComponent<MeshRenderer>();
            if (heroRenderer != null)
            {
                heroRenderer.enabled = false;
                Log.Info("英雄MeshRenderer已隐藏");
            }

            // 找到并激活Web Strand Catch Effect
            var bossScene = GameObject.Find("Boss Scene");
            if (bossScene != null)
            {
                var catchEffect = bossScene.transform.Find("Web Strand Catch Effect");
                if (catchEffect != null)
                {
                    // 设置替身位置到英雄位置
                    catchEffect.position = hero.transform.position;

                    // 匹配英雄的朝向
                    var heroScale = hero.transform.localScale;
                    var effectScale = catchEffect.localScale;
                    effectScale.x = Mathf.Sign(heroScale.x) * Mathf.Abs(effectScale.x);
                    catchEffect.localScale = effectScale;

                    // 激活替身
                    catchEffect.gameObject.SetActive(true);
                    Log.Info($"Web Strand Catch Effect已激活，位置: {catchEffect.position}");
                }
                else
                {
                    Log.Warn("Web Strand Catch Effect未找到");
                }
            }
        }

        /// <summary>
        /// 平滑移动玩家到地面Y=133.57
        /// </summary>
        private IEnumerator AnimatePlayerToGround()
        {
            var hero = HeroController.instance;
            if (hero == null) yield break;

            float startY = hero.transform.position.y;
            float targetY = 133.57f;
            float duration = 0.3f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 使用easeOutCubic缓动
                float easeT = 1f - Mathf.Pow(1f - t, 3f);
                float newY = Mathf.Lerp(startY, targetY, easeT);
                hero.transform.position = new Vector3(
                    hero.transform.position.x,
                    newY,
                    hero.transform.position.z
                );
                yield return null;
            }

            // 确保最终位置精确
            hero.transform.position = new Vector3(
                hero.transform.position.x,
                targetY,
                hero.transform.position.z
            );

            Log.Info($"玩家平滑移动到地面完成，最终Y={hero.transform.position.y:F2}");
        }

        /// <summary>
        /// 激活丝线缠绕（爬升阶段专用）
        /// </summary>
        public void ActivateSilkYankForClimb()
        {
            ActivateSilkYankAnimation();
            Log.Info("激活丝线缠绕动画（爬升阶段）");
        }

        /// <summary>
        /// 为下落做准备：恢复重力和显示
        /// </summary>
        public void PreparePlayerForFall()
        {
            var hero = HeroController.instance;
            if (hero == null) return;

            // 恢复重力
            hero.AffectedByGravity(true);

            // 恢复英雄显示
            var meshRenderer = hero.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = true;
                Log.Info("英雄MeshRenderer已恢复显示");
            }

            // 禁用Web Strand Catch Effect替身
            DeactivateCatchEffect();

            // 禁用丝线动画
            DeactivateSilkYankAnimation();

            Log.Info("玩家准备下落：重力已恢复，替身已禁用，丝线已禁用");
        }

        /// <summary>
        /// 禁用捕捉效果（禁用替身）
        /// </summary>
        private void DeactivateCatchEffect()
        {
            var bossScene = GameObject.Find("Boss Scene");
            if (bossScene != null)
            {
                var catchEffect = bossScene.transform.Find("Web Strand Catch Effect");
                if (catchEffect != null)
                {
                    catchEffect.gameObject.SetActive(false);
                    Log.Info("Web Strand Catch Effect已禁用");
                }
            }
        }

        #endregion

        /// <summary>
        /// 禁用玩家输入
        /// </summary>
        public void DisablePlayerInput()
        {
            var hero = HeroController.instance;
            if (hero != null)
            {
                hero.StopAnimationControl();
                hero.RelinquishControl();
                Log.Info("禁用玩家输入和控制");
            }
        }

        /// <summary>
        /// 启动玩家动画控制协程
        /// </summary>
        public void StartClimbPhasePlayerAnimation()
        {
            StartCoroutine(ClimbPhasePlayerAnimationCoroutine());
        }

        /// <summary>
        /// 玩家动画控制协程（穿墙下落阶段）
        /// 注意：玩家已经在前面的状态中被硬控，此处只处理下落逻辑
        /// </summary>
        private IEnumerator ClimbPhasePlayerAnimationCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("HeroController 未找到");
                yield break;
            }

            var tk2dAnimator = hero.GetComponent<tk2dSpriteAnimator>();
            var rb = hero.GetComponent<Rigidbody2D>();

            Log.Info("开始穿墙下落序列");

            // 1. 保存原始图层
            int originalLayer = hero.gameObject.layer;
            Vector3 currentPos = hero.transform.position;

            // 2. 设置穿墙图层
            hero.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            // 统一确保无敌状态
            PlayerData.instance.isInvincible = true;

            // 4. 计算X轴速度让玩家落到目标位置（X=40）
            if (rb != null)
            {
                float targetX = 40f;
                float currentX = currentPos.x;
                float fallTime = 2.3f; // 实测下落时间

                // 计算所需的X轴速度: vx = deltaX / t
                float deltaX = targetX - currentX;
                float velocityX = deltaX / fallTime;

                // 设置X轴速度，Y轴由重力控制
                rb.linearVelocity = new Vector2(velocityX, rb.linearVelocity.y);

                Log.Info($"玩家下落计算 - 当前位置:({currentX:F2}, {currentPos.y:F2}), 目标X:{targetX}, X轴速度:{velocityX:F2}");
            }

            // 5. 播放 Weak Fall 动画
            if (tk2dAnimator != null)
                tk2dAnimator.Play("Weak Fall");

            // 6. 监控 Y 轴，当 Y < 57 时恢复原始图层和无敌状态（此时还未落地）
            while (hero.transform.position.y >= 57f)
            {
                yield return null;
            }

            // 7. 恢复原始图层和无敌状态（玩家继续下落但不再穿墙）
            hero.gameObject.layer = originalLayer;
            PlayerData.instance.isInvincible = false;
            Log.Info($"玩家 Y < 57，恢复原始图层: {originalLayer}，当前位置: {hero.transform.position.y}");

            // 8. 等待落地（Y <= 53.8）
            while (hero.transform.position.y > 53.8f)
            {
                yield return null;
            }

            Log.Info($"玩家落地，最终位置: {hero.transform.position.y}");
            if (rb != null)
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

            // 9. 播放恢复动画序列
            if (tk2dAnimator != null)
            {
                tk2dAnimator.Play("FallToProstrate");
                yield return new WaitForSeconds(1f);

                tk2dAnimator.Play("ProstrateRiseToKneel");
                yield return new WaitForSeconds(1f);

                tk2dAnimator.Play("GetUpToIdle");
                yield return new WaitForSeconds(0.3f);
            }

            // 10. 恢复玩家控制和动画控制
            GameManager._instance?.inputHandler?.StartAcceptingInput();
            hero.RegainControl();
            hero.StartAnimationControl();

            // 11. 玩家恢复可控后再启动爬升阶段攻击，避免落地硬直期间被攻击
            if (_attackControl != null && !_climbAttackEventSent)
            {
                _attackControl.SendEvent("CLIMB PHASE ATTACK");
                _climbAttackEventSent = true;
                Log.Info("玩家恢复可移动，已发送 CLIMB PHASE ATTACK 事件到 Attack Control FSM");
            }

            Log.Info("玩家动画控制完成，恢复控制权和输入");
        }

        /// <summary>
        /// 监控玩家爬升进度
        /// </summary>
        public void MonitorPlayerClimbProgress()
        {
            if (_climbCompleteEventSent) return;

            var hero = HeroController.instance;
            if (hero == null) return;

            // 边界限制：X轴范围 [2, 78]
            Vector3 pos = hero.transform.position;
            if (pos.x < 2f)
            {
                hero.transform.position = new Vector3(2f, pos.y, pos.z);
                var rb = hero.GetComponent<Rigidbody2D>();
                if (rb != null && rb.linearVelocity.x < 0)
                {
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                }
            }
            else if (pos.x > 78f)
            {
                hero.transform.position = new Vector3(78f, pos.y, pos.z);
                var rb = hero.GetComponent<Rigidbody2D>();
                if (rb != null && rb.linearVelocity.x > 0)
                {
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                }
            }

            // 处理collapse_gate：当玩家X > 20时恢复GameObject但禁用Animator
            HandleCollapseGateDuringClimb(pos.x);

            // 检测玩家是否到达目标高度
            if (pos.y >= 133.5f)
            {
                if (!_climbCompleteEventSent)
                {
                    _climbCompleteEventSent = true;
                    _climbPhaseCompleted = true;
                    if (_attackControl != null && !_climbAttackEventSent)
                    {
                        _attackControl.SendEvent("CLIMB PHASE ATTACK");
                        _climbAttackEventSent = true;
                        Log.Info("玩家提前爬到顶，补发 CLIMB PHASE ATTACK 事件");
                    }
                    _phaseControl.SendEvent("CLIMB COMPLETE");
                    Log.Info("玩家到达目标高度，爬升阶段完成，发送 CLIMB COMPLETE 事件");

                    // ⚠️ 爬升完成后，启动X轴监控协程，持续检测玩家X坐标直到X > 20
                    StartCoroutine(MonitorPlayerXForCollapseGate());
                }
            }
        }

        private GameObject? _collapseGate;
        private bool _collapseGateDisabled = false;
        private bool _climbPhaseCompleted = false;

        /// <summary>
        /// 处理collapse_gate的启用/禁用逻辑
        /// </summary>
        private void HandleCollapseGateDuringClimb(float playerX)
        {
            // 首次查找collapse_gate
            if (_collapseGate == null)
            {
                var bossScene = GameObject.Find("Boss Scene");
                if (bossScene != null)
                {
                    var battleGate = bossScene.transform.Find("Battle Gate");
                    if (battleGate != null)
                    {
                        _collapseGate = battleGate.Find("boss_scene_collapse_gate")?.gameObject;
                        Log.Info($"找到collapse_gate: {(_collapseGate != null ? "成功" : "失败")}");
                    }
                }
            }

            if (_collapseGate == null) return;

            // 进入爬升阶段时禁用collapse_gate及其Animator
            if (!_collapseGateDisabled)
            {
                _collapseGate.SetActive(false);
                var animator = _collapseGate.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = false;
                }
                _collapseGateDisabled = true;
                Log.Info("已禁用collapse_gate和其Animator");
            }

            // ⚠️ 爬升完成后，检测X > 20时恢复GameObject但禁用Animator
            // 注意：这个逻辑需要持续监控，不能只在Y >= 133.5f时触发一次
            if (_climbPhaseCompleted && playerX > 20f && !_collapseGate.activeSelf)
            {
                _collapseGate.SetActive(true);
                var animator = _collapseGate.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = false;
                }
                Log.Info($"玩家X > 20，恢复collapse_gate GameObject（X={playerX:F2}），但Animator仍禁用");
            }
        }

        /// <summary>
        /// 重置爬升阶段标志
        /// </summary>
        public void ResetClimbPhaseFlags()
        {
            _climbCompleteEventSent = false;
            _climbAttackEventSent = false;  // 重置攻击事件标志
            _climbPhaseCompleted = false;
            _collapseGateDisabled = false;
            Log.Info("爬升阶段标志已重置");
        }

        /// <summary>
        /// 恢复Boss的Z轴位置到0.01（原始值）
        /// </summary>
        public void RestoreBossZPosition()
        {
            Vector3 currentPos = transform.position;
            currentPos.z = 0.01f;
            transform.position = currentPos;
            Log.Info($"恢复Boss Z轴到0.01，当前位置: {transform.position}");
        }

        /// <summary>
        /// 快速移动Boss回到战斗场地
        /// </summary>
        public void MoveBossBackToArena()
        {
            StartCoroutine(MoveBossBackToArenaCoroutine());
        }

        private IEnumerator MoveBossBackToArenaCoroutine()
        {
            Log.Info("开始快速移动Boss回到战斗场地");

            Vector3 startPos = transform.position;
            Vector3 targetPos = new Vector3(startPos.x, 136f, startPos.z); // 回到Y=136战斗区域
            float duration = 1.5f; // 1.5秒快速移动
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                transform.position = Vector3.Lerp(startPos, targetPos, t);
                yield return null;
            }

            transform.position = targetPos;
            Log.Info($"Boss已回到战斗场地: {transform.position}");

            // ⚠️ Boss返回完成后恢复碰撞器
            var bossBehavior = GetComponent<BossBehavior>();
            if (bossBehavior != null)
            {
                bossBehavior.EnableBossCollider();
            }

            // ⚠️ Boss完全恢复后，发送 CLIMB PHASE END 到 Attack Control 恢复攻击
            if (_attackControl != null)
            {
                _attackControl.SendEvent("CLIMB PHASE END");
                Log.Info("Boss完全恢复，已发送 CLIMB PHASE END 到 Attack Control 恢复攻击");
            }
        }

        /// <summary>
        /// 监控玩家X坐标，直到X > 20时恢复collapse_gate
        /// </summary>
        private IEnumerator MonitorPlayerXForCollapseGate()
        {
            var hero = HeroController.instance;
            if (hero == null || _collapseGate == null)
            {
                Log.Warn("无法监控玩家X坐标或collapse_gate为null");
                yield break;
            }

            Log.Info("开始监控玩家X坐标以恢复collapse_gate");

            // 持续监控直到玩家X > 20或collapse_gate已恢复
            while (hero != null && _collapseGate != null)
            {
                float playerX = hero.transform.position.x;

                // 如果玩家X > 20且collapse_gate未激活，则恢复它
                if (playerX > 20f && !_collapseGate.activeSelf)
                {
                    _collapseGate.SetActive(true);
                    var animator = _collapseGate.GetComponent<Animator>();
                    if (animator != null)
                    {
                        animator.enabled = false;
                    }
                    Log.Info($"玩家X坐标 > 20 ({playerX:F2})，已恢复collapse_gate GameObject，Animator保持禁用");
                    yield break; // 恢复后退出监控
                }

                yield return new WaitForSeconds(0.1f); // 每0.1秒检查一次
            }
        }
        #endregion

        #region 丝线缠绕动画
        /// <summary>
        /// 启用丝线缠绕动画，在五个位置显示（上、左上、右上、左下、右下）
        /// </summary>
        private void ActivateSilkYankAnimation()
        {
            if (!_silkYankInitialized || _silkYankClones[0] == null)
            {
                Log.Error("丝线缠绕未初始化，无法启用动画");
                return;
            }

            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("HeroController未找到，无法启用丝线缠绕");
                return;
            }

            Vector3 heroPos = hero.transform.position;
            float offsetDistance = 10f;  // 偏移距离

            // 五个位置：上、左上、右上、左下、右下
            Vector3[] positions = new Vector3[]
            {
                heroPos + new Vector3(0f, offsetDistance, 0f),                 // 上
                heroPos + new Vector3(-offsetDistance, offsetDistance, 0f),    // 左上
                heroPos + new Vector3(offsetDistance, offsetDistance, 0f),     // 右上
                heroPos + new Vector3(-offsetDistance, -offsetDistance, 0f),   // 左下
                heroPos + new Vector3(offsetDistance, -offsetDistance, 0f)     // 右下
            };

            // 五个旋转角度（180°是垂直向下，统一朝向玩家中心）
            float[] rotations = new float[]
            {
                180f,   // 上 -> 向下指向玩家
                -45f,   // 左上 -> 向右下指向玩家
                -135f,  // 右上 -> 向左下指向玩家
                45f,    // 左下 -> 向右上指向玩家
                135f    // 右下 -> 向左上指向玩家
            };

            for (int i = 0; i < 5; i++)
            {
                if (_silkYankClones[i] != null)
                {
                    // 设置位置和旋转
                    _silkYankClones[i].transform.position = positions[i];
                    _silkYankClones[i].transform.rotation = Quaternion.Euler(0f, 0f, rotations[i]);

                    // 启用物体，动画会自动播放
                    _silkYankClones[i].SetActive(true);

                    Log.Info($"启用丝线缠绕 {i}: 位置={positions[i]}, 旋转={rotations[i]}°");
                }
            }

            Log.Info("丝线缠绕动画启用完成（5个位置）");
        }

        /// <summary>
        /// 禁用丝线缠绕动画
        /// </summary>
        private void DeactivateSilkYankAnimation()
        {
            for (int i = 0; i < 5; i++)
            {
                if (_silkYankClones[i] != null)
                {
                    _silkYankClones[i].SetActive(false);
                }
            }
            Log.Info("丝线缠绕动画已禁用");
        }

        /// <summary>
        /// 更新丝线位置跟随玩家（协程）
        /// </summary>
        /// <param name="duration">跟随持续时间</param>
        private IEnumerator UpdateSilkYankPositions(float duration)
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController未找到，无法更新丝线位置");
                yield break;
            }

            float elapsed = 0f;
            float offsetDistance = 10f;  // 偏移距离

            // 五个旋转角度（180°是垂直向下，统一朝向玩家中心）
            float[] rotations = new float[]
            {
                180f,   // 上 -> 向下指向玩家
                -45f,   // 左上 -> 向右下指向玩家
                -135f,  // 右上 -> 向左下指向玩家
                45f,    // 左下 -> 向右上指向玩家
                135f    // 右下 -> 向左上指向玩家
            };

            while (elapsed < duration)
            {
                if (hero != null)
                {
                    Vector3 heroPos = hero.transform.position;

                    // 五个位置：上、左上、右上、左下、右下
                    Vector3[] positions = new Vector3[]
                    {
                        heroPos + new Vector3(0f, offsetDistance, 0f),                 // 上
                        heroPos + new Vector3(-offsetDistance, offsetDistance, 0f),    // 左上
                        heroPos + new Vector3(offsetDistance, offsetDistance, 0f),     // 右上
                        heroPos + new Vector3(-offsetDistance, -offsetDistance, 0f),   // 左下
                        heroPos + new Vector3(offsetDistance, -offsetDistance, 0f)     // 右下
                    };

                    // 更新所有丝线位置
                    for (int i = 0; i < 5; i++)
                    {
                        if (_silkYankClones[i] != null && _silkYankClones[i].activeSelf)
                        {
                            _silkYankClones[i].transform.position = positions[i];
                            _silkYankClones[i].transform.rotation = Quaternion.Euler(0f, 0f, rotations[i]);
                        }
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Log.Info($"丝线跟随完成，持续时间: {duration}秒");
        }
        #endregion
    }
}