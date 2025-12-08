using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;

namespace AnySilkBoss.Source.Tools
{
    /// <summary>
    /// FSM状态创建工具类 - 提供统一的状态创建、添加和管理方法
    /// </summary>
    public static class FsmStateBuilder
    {
        #region 基础创建方法

        /// <summary>
        /// 创建一个FSM状态
        /// </summary>
        /// <param name="fsm">目标Fsm实例</param>
        /// <param name="name">状态名称</param>
        /// <param name="description">状态描述（可选）</param>
        /// <returns>新创建的FsmState</returns>
        public static FsmState CreateState(Fsm fsm, string name, string description = "")
        {
            return new FsmState(fsm)
            {
                Name = name,
                Description = description
            };
        }

        /// <summary>
        /// 从PlayMakerFSM创建一个状态
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="name">状态名称</param>
        /// <param name="description">状态描述（可选）</param>
        /// <returns>新创建的FsmState</returns>
        public static FsmState CreateState(PlayMakerFSM pmFsm, string name, string description = "")
        {
            return CreateState(pmFsm.Fsm, name, description);
        }

        #endregion

        #region 创建并添加到FSM

        /// <summary>
        /// 创建状态并立即添加到FSM的States数组
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="name">状态名称</param>
        /// <param name="description">状态描述（可选）</param>
        /// <returns>新创建并已添加的FsmState</returns>
        public static FsmState CreateAndAddState(PlayMakerFSM pmFsm, string name, string description = "")
        {
            var state = CreateState(pmFsm.Fsm, name, description);
            AddStateToFsm(pmFsm, state);
            return state;
        }

        /// <summary>
        /// 将状态添加到FSM的States数组
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="state">要添加的状态</param>
        public static void AddStateToFsm(PlayMakerFSM pmFsm, FsmState state)
        {
            var states = pmFsm.Fsm.States.ToList();
            states.Add(state);
            pmFsm.Fsm.States = states.ToArray();
        }

        /// <summary>
        /// 将多个状态添加到FSM的States数组
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="statesToAdd">要添加的状态数组</param>
        public static void AddStatesToFsm(PlayMakerFSM pmFsm, params FsmState[] statesToAdd)
        {
            var states = pmFsm.Fsm.States.ToList();
            states.AddRange(statesToAdd);
            pmFsm.Fsm.States = states.ToArray();
        }

        #endregion

        #region 批量创建

        /// <summary>
        /// 状态定义结构
        /// </summary>
        public struct StateDefinition
        {
            public string Name;
            public string Description;

            public StateDefinition(string name, string description = "")
            {
                Name = name;
                Description = description;
            }
        }

        /// <summary>
        /// 批量创建状态（不添加到FSM）
        /// </summary>
        /// <param name="fsm">目标Fsm实例</param>
        /// <param name="definitions">状态定义数组</param>
        /// <returns>创建的状态数组</returns>
        public static FsmState[] CreateStates(Fsm fsm, params StateDefinition[] definitions)
        {
            var states = new FsmState[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                states[i] = CreateState(fsm, definitions[i].Name, definitions[i].Description);
            }
            return states;
        }

        /// <summary>
        /// 批量创建状态（使用元组语法）
        /// </summary>
        /// <param name="fsm">目标Fsm实例</param>
        /// <param name="definitions">状态定义元组数组 (name, description)</param>
        /// <returns>创建的状态数组</returns>
        public static FsmState[] CreateStates(Fsm fsm, params (string name, string description)[] definitions)
        {
            var states = new FsmState[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                states[i] = CreateState(fsm, definitions[i].name, definitions[i].description);
            }
            return states;
        }

        /// <summary>
        /// 批量创建并添加状态到FSM
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="definitions">状态定义元组数组 (name, description)</param>
        /// <returns>创建的状态数组</returns>
        public static FsmState[] CreateAndAddStates(PlayMakerFSM pmFsm, params (string name, string description)[] definitions)
        {
            var states = CreateStates(pmFsm.Fsm, definitions);
            AddStatesToFsm(pmFsm, states);
            return states;
        }

        #endregion

        #region 状态查询辅助

        /// <summary>
        /// 根据名称查找状态
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="stateName">状态名称</param>
        /// <returns>找到的状态，未找到返回null</returns>
        public static FsmState? FindState(PlayMakerFSM pmFsm, string stateName)
        {
            return pmFsm.FsmStates.FirstOrDefault(s => s.Name == stateName);
        }

        /// <summary>
        /// 检查状态是否存在
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="stateName">状态名称</param>
        /// <returns>是否存在</returns>
        public static bool StateExists(PlayMakerFSM pmFsm, string stateName)
        {
            return pmFsm.FsmStates.Any(s => s.Name == stateName);
        }

        /// <summary>
        /// 获取或创建状态（如果不存在则创建）
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="name">状态名称</param>
        /// <param name="description">状态描述</param>
        /// <returns>已存在或新创建的状态</returns>
        public static FsmState GetOrCreateState(PlayMakerFSM pmFsm, string name, string description = "")
        {
            var existing = FindState(pmFsm, name);
            if (existing != null) return existing;
            return CreateAndAddState(pmFsm, name, description);
        }

        #endregion

        #region 转换辅助

        /// <summary>
        /// 创建一个简单的转换（使用FINISHED事件）
        /// </summary>
        /// <param name="targetState">目标状态</param>
        /// <returns>FsmTransition</returns>
        public static FsmTransition CreateFinishedTransition(FsmState targetState)
        {
            return new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = targetState.Name,
                toFsmState = targetState
            };
        }

        /// <summary>
        /// 创建一个自定义事件转换
        /// </summary>
        /// <param name="fsmEvent">触发事件</param>
        /// <param name="targetState">目标状态</param>
        /// <returns>FsmTransition</returns>
        public static FsmTransition CreateTransition(FsmEvent fsmEvent, FsmState targetState)
        {
            return new FsmTransition
            {
                FsmEvent = fsmEvent,
                toState = targetState.Name,
                toFsmState = targetState
            };
        }

        /// <summary>
        /// 为状态设置单一FINISHED转换
        /// </summary>
        /// <param name="state">源状态</param>
        /// <param name="targetState">目标状态</param>
        public static void SetFinishedTransition(FsmState state, FsmState targetState)
        {
            state.Transitions = new FsmTransition[] { CreateFinishedTransition(targetState) };
        }

        /// <summary>
        /// 为状态添加转换（保留现有转换）
        /// </summary>
        /// <param name="state">源状态</param>
        /// <param name="transition">要添加的转换</param>
        public static void AddTransition(FsmState state, FsmTransition transition)
        {
            var transitions = state.Transitions?.ToList() ?? new List<FsmTransition>();
            transitions.Add(transition);
            state.Transitions = transitions.ToArray();
        }

        #endregion

        #region 事件辅助

        /// <summary>
        /// 获取或创建事件
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="eventName">事件名称</param>
        /// <returns>FsmEvent</returns>
        public static FsmEvent GetOrCreateEvent(PlayMakerFSM pmFsm, string eventName)
        {
            var events = pmFsm.Fsm.Events.ToList();
            var existing = events.FirstOrDefault(e => e.Name == eventName);
            if (existing != null) return existing;

            var newEvent = new FsmEvent(eventName);
            events.Add(newEvent);
            pmFsm.Fsm.Events = events.ToArray();
            return newEvent;
        }

        /// <summary>
        /// 批量注册事件
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        /// <param name="eventNames">事件名称数组</param>
        /// <returns>创建的事件数组</returns>
        public static FsmEvent[] RegisterEvents(PlayMakerFSM pmFsm, params string[] eventNames)
        {
            var events = pmFsm.Fsm.Events.ToList();
            var result = new FsmEvent[eventNames.Length];

            for (int i = 0; i < eventNames.Length; i++)
            {
                var existing = events.FirstOrDefault(e => e.Name == eventNames[i]);
                if (existing != null)
                {
                    result[i] = existing;
                }
                else
                {
                    result[i] = new FsmEvent(eventNames[i]);
                    events.Add(result[i]);
                }
            }

            pmFsm.Fsm.Events = events.ToArray();
            return result;
        }

        #endregion

        #region 初始化辅助

        /// <summary>
        /// 重新初始化FSM数据和事件（在添加新状态/事件/转换后必须调用）
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        public static void ReinitializeFsm(PlayMakerFSM pmFsm)
        {
            // pmFsm.Fsm.InitData();
        }

        /// <summary>
        /// 重新初始化FSM变量
        /// </summary>
        /// <param name="pmFsm">PlayMakerFSM组件</param>
        public static void ReinitializeFsmVariables(PlayMakerFSM pmFsm)
        {
            pmFsm.FsmVariables.Init();
        }

        #endregion
    }
}
