using System.IO;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace AnySilkBoss.Source.Tools
{
    internal static class FsmAnalyzer
    {
        public static void WriteFsmReport(PlayMakerFSM fsm, string outputPath)
        {
            if (fsm == null) return;

            var sb = new StringBuilder();

            sb.AppendLine($"=== FSM 报告: {fsm.FsmName} ===");
            sb.AppendLine($"当前状态: {fsm.ActiveStateName}");

            // 状态
            var states = fsm.FsmStates;
            sb.AppendLine($"\n--- 所有状态 ({states.Length}个) ---");
            foreach (var state in states)
            {
                sb.AppendLine($"状态: {state.Name}");
            }

            // 事件
            var events = fsm.FsmEvents;
            sb.AppendLine($"\n--- 所有事件 ({events.Length}个) ---");
            foreach (var evt in events)
            {
                sb.AppendLine($"事件: {evt.Name}");
            }

            // 全局转换
            var globalTransitions = fsm.FsmGlobalTransitions;
            sb.AppendLine($"\n--- 所有全局转换 ({globalTransitions.Length}个) ---");
            foreach (var gt in globalTransitions)
            {
                sb.AppendLine($"全局转换: {gt.FsmEvent?.Name} -> {gt.toState}");
            }

            // 各状态详情
            sb.AppendLine($"\n--- 各状态转换与动作 ---");
            foreach (var state in states)
            {
                sb.AppendLine($"状态 {state.Name} 的转换:");
                if (state.Transitions != null && state.Transitions.Length > 0)
                {
                    foreach (var tr in state.Transitions)
                    {
                        sb.AppendLine($"  {tr.FsmEvent?.Name} -> {tr.toState}");
                    }
                }
                else
                {
                    sb.AppendLine("  (无转换)");
                }

                // 动作与变量简析
                if (state.Actions != null && state.Actions.Length > 0)
                {
                    sb.AppendLine($"动作 ({state.Actions.Length}个):");
                    for (int i = 0; i < state.Actions.Length; i++)
                    {
                        var action = state.Actions[i];
                        sb.AppendLine($"  [{i}] {action.GetType().Name}");
                        AnalyzeActionVariables(sb,action, i);
                    }
                }
                else
                {
                    sb.AppendLine("动作: 无");
                }
            }

            // 变量
            var vars = fsm.FsmVariables;
            sb.AppendLine($"\n--- 变量 ---");
            sb.AppendLine($"Bool: {vars.BoolVariables.Length}, Int: {vars.IntVariables.Length}, Float: {vars.FloatVariables.Length}, String: {vars.StringVariables.Length}");
            foreach (var v in vars.FloatVariables) sb.AppendLine($"Float {v.Name} = {v.Value}");
            foreach (var v in vars.BoolVariables) sb.AppendLine($"Bool {v.Name} = {v.Value}");
            foreach (var v in vars.IntVariables) sb.AppendLine($"Int {v.Name} = {v.Value}");
            foreach (var v in vars.StringVariables) sb.AppendLine($"String {v.Name} = {v.Value}");

            // 写文件
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }
        private static void AnalyzeActionVariables(StringBuilder sb, FsmStateAction action, int actionIndex)
        {
            try
            {
                var actionType = action.GetType();
                var fields = actionType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                sb.AppendLine($"    行为 {actionIndex} 的变量:");
                
                foreach (var field in fields)
                {
                    var value = field.GetValue(action);
                    if (value != null)
                    {
                        sb.AppendLine($"      {field.Name}: {value} ({value.GetType().Name})");
                        
                        // 详细分析不同类型的变量
                        if (value is FsmFloat fsmFloat)
                        {
                            sb.AppendLine($"        -> FsmFloat值: {fsmFloat.Value}");
                            if (fsmFloat.UseVariable && fsmFloat.Name != null)
                            {
                                sb.AppendLine($"        -> 使用变量: {fsmFloat.Name}");
                            }
                        }
                        else if (value is FsmString fsmString)
                        {
                            sb.AppendLine($"        -> FsmString值: {fsmString.Value}");
                            if (fsmString.UseVariable && fsmString.Name != null)
                            {
                                sb.AppendLine($"        -> 使用变量: {fsmString.Name}");
                            }
                        }
                        else if (value is FsmBool fsmBool)
                        {
                            sb.AppendLine($"        -> FsmBool值: {fsmBool.Value}");
                            if (fsmBool.UseVariable && fsmBool.Name != null)
                            {
                                sb.AppendLine($"        -> 使用变量: {fsmBool.Name}");
                            }
                        }
                        else if (value is FsmInt fsmInt)
                        {
                            sb.AppendLine($"        -> FsmInt值: {fsmInt.Value}");
                            if (fsmInt.UseVariable && fsmInt.Name != null)
                            {
                                sb.AppendLine($"        -> 使用变量: {fsmInt.Name}");
                            }
                        }
                        else if (value is FsmGameObject fsmGameObject)
                        {
                            AnalyzeFsmGameObject(sb, fsmGameObject, action);
                        }
                        else if (value is FsmOwnerDefault fsmOwnerDefault)
                        {
                            AnalyzeFsmOwnerDefault(sb, fsmOwnerDefault, action);
                        }
                        else if (value is FsmEvent fsmEvent)
                        {
                            sb.AppendLine($"        -> FsmEvent: {fsmEvent.Name}");
                        }
                        else if (value is GameObject gameObject)
                        {
                            sb.AppendLine($"        -> GameObject名称: {gameObject.name}");
                            sb.AppendLine($"        -> GameObject路径: {GetGameObjectPath(gameObject)}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"    分析行为 {actionIndex} 变量时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 深度分析 FsmGameObject 类型
        /// </summary>
        private static void AnalyzeFsmGameObject(StringBuilder sb, FsmGameObject fsmGameObject, FsmStateAction action)
        {
            sb.AppendLine($"        -> FsmGameObject详情:");
            
            // 检查是否使用变量
            if (fsmGameObject.UseVariable)
            {
                sb.AppendLine($"           使用变量: {fsmGameObject.Name}");
            }
            
            // 尝试获取实际的 GameObject 值
            var actualGameObject = fsmGameObject.Value;
            if (actualGameObject != null)
            {
                sb.AppendLine($"           GameObject名称: {actualGameObject.name}");
                sb.AppendLine($"           GameObject路径: {GetGameObjectPath(actualGameObject)}");
                sb.AppendLine($"           是否激活: {actualGameObject.activeSelf}");
                
                // 列出主要组件
                var components = actualGameObject.GetComponents<Component>();
                if (components.Length > 0)
                {
                    sb.AppendLine($"           组件列表: {string.Join(", ", components.Select(c => c.GetType().Name))}");
                }
            }
            else
            {
                sb.AppendLine($"           GameObject值: null (可能在运行时设置)");
            }
        }

        /// <summary>
        /// 深度分析 FsmOwnerDefault 类型
        /// </summary>
        private static void AnalyzeFsmOwnerDefault(StringBuilder sb, FsmOwnerDefault fsmOwnerDefault, FsmStateAction action)
        {
            sb.AppendLine($"        -> FsmOwnerDefault详情:");
            
            // 获取 OwnerOption
            try
            {
                var ownerOptionField = fsmOwnerDefault.GetType().GetField("OwnerOption");
                if (ownerOptionField != null)
                {
                    var ownerOption = ownerOptionField.GetValue(fsmOwnerDefault);
                    sb.AppendLine($"           OwnerOption: {ownerOption}");
                }
            }
            catch { }
            
            // 尝试获取 GameObject 属性
            try
            {
                var gameObjectProp = fsmOwnerDefault.GetType().GetProperty("GameObject");
                if (gameObjectProp != null)
                {
                    var fsmGameObject = gameObjectProp.GetValue(fsmOwnerDefault) as FsmGameObject;
                    if (fsmGameObject != null && fsmGameObject.Value != null)
                    {
                        sb.AppendLine($"           指定GameObject名称: {fsmGameObject.Value.name}");
                        sb.AppendLine($"           指定GameObject路径: {GetGameObjectPath(fsmGameObject.Value)}");
                    }
                }
            }
            catch { }
            
            // 尝试从 action.Fsm 获取 Owner
            if (action.Fsm != null && action.Fsm.GameObject != null)
            {
                sb.AppendLine($"           Owner(FSM宿主)名称: {action.Fsm.GameObject.name}");
                sb.AppendLine($"           Owner(FSM宿主)路径: {GetGameObjectPath(action.Fsm.GameObject)}");
            }
        }

        /// <summary>
        /// 获取 GameObject 的完整路径
        /// </summary>
        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "null";
            
            string path = obj.name;
            Transform parent = obj.transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
    }
}


