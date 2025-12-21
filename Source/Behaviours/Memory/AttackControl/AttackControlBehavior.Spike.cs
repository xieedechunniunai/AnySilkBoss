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
    /// <summary>
    /// Attack Control 行为 - 地刺攻击模块
    /// 
    /// 核心逻辑：
    /// 1. 每个阶段维持对应数量的地刺（P1=1个, P2=2个...P6=6个）
    /// 2. 通过 SpikeAttackPending 变量触发新地刺
    /// 3. 地刺自身在 Spikes End 状态形成循环
    /// </summary>
    internal partial class MemoryAttackControlBehavior
    {
        #region 地刺相关字段
        
        [Header("地刺配置")]
        private GameObject? _spikeFloorsParent;       // Spike Floors 父物体
        private FsmBool? _spikeAttackPending;         // 是否有待触发的地刺攻击
        private FsmInt? _currentPhaseVar;             // 当前阶段变量
        private int _activeSpikeCount = 0;            // 当前活跃地刺数量
        private bool _spikeSystemInitialized = false;
        
        #endregion

        #region 地刺初始化

        /// <summary>
        /// 初始化地刺系统
        /// </summary>
        private void InitializeSpikeSystem()
        {
            if (_spikeSystemInitialized) return;
            if (_attackControlFsm == null) return;

            // 查找 Spike Floors 父物体
            FindSpikeFloorsParent();

            if (_spikeFloorsParent == null)
            {
                Log.Warn("[AttackControl] 未找到 Spike Floors 父物体，地刺系统未初始化");
                return;
            }

            // 创建 FSM 变量
            CreateSpikeVariables();

            // 初始化所有地刺行为组件
            MemorySpikeFloorBehavior.InitializeAllSpikeFloors(_spikeFloorsParent);

            // 修改 Rubble Attack? 状态添加地刺触发检查
            ModifyRubbleAttackStateForSpike();

            _spikeSystemInitialized = true;
            Log.Info("[AttackControl] 地刺系统初始化完成");
        }

        /// <summary>
        /// 查找 Spike Floors 父物体
        /// </summary>
        private void FindSpikeFloorsParent()
        {
            if (_bossScene == null) return;

            // 从 Boss Scene 查找 Spike Floors
            var spikeFloorsTransform = _bossScene.transform.Find("Spike Floors");
            if (spikeFloorsTransform != null)
            {
                _spikeFloorsParent = spikeFloorsTransform.gameObject;
                Log.Info($"[AttackControl] 找到 Spike Floors: {_spikeFloorsParent.name}");
            }
            else
            {
                // 尝试使用 FSM 变量中的引用
                var spikeFloorsVar = _attackControlFsm?.FsmVariables.GetFsmGameObject("Spike Floors");
                if (spikeFloorsVar != null && spikeFloorsVar.Value != null)
                {
                    _spikeFloorsParent = spikeFloorsVar.Value;
                    Log.Info($"[AttackControl] 从 FSM 变量找到 Spike Floors: {_spikeFloorsParent.name}");
                }
            }
        }

        /// <summary>
        /// 创建地刺相关 FSM 变量
        /// </summary>
        private void CreateSpikeVariables()
        {
            if (_attackControlFsm == null) return;

            // 创建或获取 SpikeAttackPending 变量
            _spikeAttackPending = _attackControlFsm.FsmVariables.FindFsmBool("SpikeAttackPending");
            if (_spikeAttackPending == null)
            {
                _spikeAttackPending = new FsmBool("SpikeAttackPending") { Value = false };
                var boolVars = _attackControlFsm.FsmVariables.BoolVariables.ToList();
                boolVars.Add(_spikeAttackPending);
                _attackControlFsm.FsmVariables.BoolVariables = boolVars.ToArray();
                Log.Info("[AttackControl] 创建了 SpikeAttackPending 变量");
            }

            // 创建或获取当前阶段变量
            _currentPhaseVar = _attackControlFsm.FsmVariables.FindFsmInt("CurrentPhase");
            if (_currentPhaseVar == null)
            {
                _currentPhaseVar = new FsmInt("CurrentPhase") { Value = 1 };
                var intVars = _attackControlFsm.FsmVariables.IntVariables.ToList();
                intVars.Add(_currentPhaseVar);
                _attackControlFsm.FsmVariables.IntVariables = intVars.ToArray();
                Log.Info("[AttackControl] 创建了 CurrentPhase 变量");
            }

            _attackControlFsm.FsmVariables.Init();
        }

        /// <summary>
        /// 修改 FSM 以支持新地刺系统：
        /// 1. 禁用原版地刺逻辑（Rubble Attack?）
        /// 2. 在 Attack Choice 添加地刺检测
        /// 3. 创建地刺触发状态
        /// </summary>
        private void ModifyRubbleAttackStateForSpike()
        {
            if (_attackControlFsm == null) return;

            // 1. 禁用原版地刺逻辑
            DisableOriginalSpikeLift();

            // 2. 创建地刺触发状态
            CreateSpikeTriggerState();

            // 3. 在 Attack Choice 添加地刺检测
            ModifyAttackChoiceForSpike();

            Log.Info("[AttackControl] 新地刺系统初始化完成");
        }

        /// <summary>
        /// 禁用原版地刺逻辑
        /// </summary>
        private void DisableOriginalSpikeLift()
        {
            var rubbleAttackState = _rubbleAttackQuestionState;
            if (rubbleAttackState == null)
            {
                Log.Warn("[AttackControl] 未找到 Rubble Attack? 状态");
                return;
            }

            // 移除 SPIKE LIFT 跳转
            RemoveSpikeTransitions(rubbleAttackState);

            // 移除原版地刺相关 Actions
            RemoveSpikeActions(rubbleAttackState);

            Log.Info("[AttackControl] 已禁用 Rubble Attack? 状态的原版地刺逻辑");
        }

        #region 地刺触发状态

        private FsmState? _spikeTriggerState;
        private FsmEvent? _spikeTriggerEvent;

        /// <summary>
        /// 创建地刺触发状态
        /// </summary>
        private void CreateSpikeTriggerState()
        {
            if (_attackControlFsm == null) return;

            // 创建 SPIKE TRIGGER 事件
            _spikeTriggerEvent = FsmEvent.GetFsmEvent("SPIKE TRIGGER");

            // 创建地刺触发状态
            _spikeTriggerState = CreateState(_attackControlFsm.Fsm, "Spike Trigger", "触发地刺攻击并重置标记");

            var actions = new List<FsmStateAction>();

            // 1. 调用触发地刺方法
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteSpikeTrigger") { Value = "ExecuteSpikeTrigger" },
                parameters = new FsmVar[0]
            });

            // 2. 设置 SpikeAttackPending = false
            actions.Add(new SetBoolValue
            {
                boolVariable = _spikeAttackPending,
                boolValue = new FsmBool(false),
                everyFrame = false
            });

            // 3. 短暂等待后返回 Attack Choice
            actions.Add(new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            _spikeTriggerState.Actions = actions.ToArray();

            // 设置跳转：FINISHED -> Attack Choice
            var attackChoiceState = _attackChoiceState;
            if (attackChoiceState != null)
            {
                SetFinishedTransition(_spikeTriggerState, attackChoiceState);
            }

            // 添加状态到 FSM
            AddStateToFsm(_attackControlFsm, _spikeTriggerState);

            Log.Info("[AttackControl] 创建了 Spike Trigger 状态");
        }

        /// <summary>
        /// 修改 Attack Choice 状态，添加地刺检测
        /// </summary>
        private void ModifyAttackChoiceForSpike()
        {
            if (_attackControlFsm == null || _spikeTriggerEvent == null) return;

            var attackChoiceState = _attackChoiceState;
            if (attackChoiceState == null)
            {
                Log.Warn("[AttackControl] 未找到 Attack Choice 状态");
                return;
            }
            FsmStateBuilder.AddTransition(attackChoiceState, CreateTransition(_spikeTriggerEvent, _spikeTriggerState!));

            // 在动作开头添加 BoolTest 检测 SpikeAttackPending
            var actions = attackChoiceState.Actions.ToList();
            var boolTest = new BoolTest
            {
                boolVariable = _spikeAttackPending,
                isTrue = _spikeTriggerEvent,
                isFalse = null,
                everyFrame = false
            };
            actions.Insert(0, boolTest);
            attackChoiceState.Actions = actions.ToArray();

            Log.Info("[AttackControl] Attack Choice 状态已添加地刺检测");
        }

        /// <summary>
        /// 执行地刺触发（供 FSM 调用）
        /// </summary>
        public void ExecuteSpikeTrigger()
        {
            if (_spikeFloorsParent == null)
            {
                FindSpikeFloorsParent();
                if (_spikeFloorsParent == null) return;
            }

            // 获取当前阶段
            int currentPhase = _currentPhaseVar?.Value ?? 1;

            // 获取当前活跃地刺数量
            int currentActiveCount = GetActiveSpikeCount();

            // 计算需要触发的数量
            int targetCount = currentPhase;
            int toTrigger = targetCount - currentActiveCount;

            if (toTrigger > 0)
            {
                MemorySpikeFloorBehavior.TriggerSpikeAttacks(_spikeFloorsParent, toTrigger);
                Log.Info($"[AttackControl] 触发 {toTrigger} 个地刺（阶段 P{currentPhase}，当前活跃 {currentActiveCount}）");
            }
            else
            {
                Log.Debug($"[AttackControl] 地刺已达目标数量（阶段 P{currentPhase}，活跃 {currentActiveCount}）");
            }
        }

        #endregion

        /// <summary>
        /// 移除状态的 SPIKE LIFT 跳转
        /// </summary>
        private void RemoveSpikeTransitions(FsmState state)
        {
            if (state.Transitions == null) return;

            var newTransitions = state.Transitions
                .Where(t => t.EventName != "SPIKE LIFT")
                .ToArray();

            if (newTransitions.Length < state.Transitions.Length)
            {
                state.Transitions = newTransitions;
                Log.Info($"[AttackControl] 移除了 {state.Name} 状态的 SPIKE LIFT 跳转");
            }
        }

        /// <summary>
        /// 移除状态中的原版地刺相关 Actions
        /// </summary>
        private void RemoveSpikeActions(FsmState state)
        {
            if (state.Actions == null) return;

            var actionsToKeep = new List<FsmStateAction>();
            int removedCount = 0;

            foreach (var action in state.Actions)
            {
                bool shouldRemove = false;

                // 移除 BoolTestMulti（触发 SPIKE LIFT 事件）
                if (action is BoolTestMulti boolTestMulti)
                {
                    if (boolTestMulti.trueEvent?.Name == "SPIKE LIFT")
                    {
                        shouldRemove = true;
                        Log.Debug("[AttackControl] 移除 BoolTestMulti (SPIKE LIFT)");
                    }
                }

                // 移除检查 Can Spike Pull 的 BoolTest
                if (action is BoolTest boolTest)
                {
                    // 检查是否是 Can Spike Pull 相关的 BoolTest
                    if (boolTest.boolVariable?.Name == "Can Spike Pull")
                    {
                        shouldRemove = true;
                        Log.Debug("[AttackControl] 移除 BoolTest (Can Spike Pull)");
                    }
                }

                // 处理 SendRandomEventV4：移除其中的 SPIKE LIFT 事件
                if (action is SendRandomEventV4 sendRandomEvent)
                {
                    RemoveSpikeLiftFromSendRandomEventV4(sendRandomEvent);
                    // 不移除 action 本身，只移除其中的 SPIKE LIFT 事件
                }

                if (shouldRemove)
                {
                    removedCount++;
                }
                else
                {
                    actionsToKeep.Add(action);
                }
            }

            if (removedCount > 0)
            {
                state.Actions = actionsToKeep.ToArray();
                Log.Info($"[AttackControl] 从 {state.Name} 状态移除了 {removedCount} 个原版地刺 Actions");
            }
        }

        /// <summary>
        /// 从 SendRandomEventV4 中移除 SPIKE LIFT 事件
        /// </summary>
        private void RemoveSpikeLiftFromSendRandomEventV4(SendRandomEventV4 action)
        {
            if (action.events == null || action.events.Length == 0) return;

            // 查找 SPIKE LIFT 事件的索引
            int spikeLiftIndex = -1;
            for (int i = 0; i < action.events.Length; i++)
            {
                if (action.events[i]?.Name == "SPIKE LIFT")
                {
                    spikeLiftIndex = i;
                    break;
                }
            }

            if (spikeLiftIndex < 0) return;
            var events = new FsmEvent[2];
            events[0] = FsmEvent.GetFsmEvent("RUBBLE PULL");
            events[1] = FsmEvent.Finished;
            action.events = events;
            Log.Info("[AttackControl] 从 SendRandomEventV4 移除了 SPIKE LIFT 事件");
        }

        /// <summary>
        /// 禁用原版地刺相关状态（可选，使其不被执行）
        /// </summary>
        private void DisableOriginalSpikeStates()
        {
            if (_attackControlFsm == null) return;

            string[] spikeStateNames = new[]
            {
                "Spike Lift Type",
                "Spike Lift Aim",
                "Spike Lift Aim 2",
                "Spike Intro Pause",
                "Spike Lift Intro"
            };

            foreach (var stateName in spikeStateNames)
            {
                var state = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == stateName);
                if (state != null)
                {
                    // 清空状态的所有动作（使其成为空状态）
                    // 不删除状态本身，以避免 FSM 结构问题
                    // state.Actions = new FsmStateAction[0];
                    Log.Debug($"[AttackControl] 原版地刺状态 {stateName} 已被跳过（不再可达）");
                }
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取当前活跃（正在攻击）的地刺数量
        /// </summary>
        private int GetActiveSpikeCount()
        {
            if (_spikeFloorsParent == null) return 0;

            int count = 0;
            var behaviors = _spikeFloorsParent.GetComponentsInChildren<MemorySpikeFloorBehavior>();
            foreach (var behavior in behaviors)
            {
                if (!behavior.IsIdle())
                {
                    count++;
                }
            }

            return count;
        }

        #endregion
    }
}
