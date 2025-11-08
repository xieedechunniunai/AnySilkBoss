using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// 手部控制Behavior - 管理单个手部对象及其控制的3根Finger Blade
    /// </summary>
    internal class HandControlBehavior : MonoBehaviour
    {
        [Header("手部配置")]
        public string handName = "";  // 手部名称（Hand L 或 Hand R）

        [Header("Finger Blade配置")]
        public Transform[] fingerBlades = new Transform[3]; // 3根Finger Blade的Transform
        public FingerBladeBehavior[] fingerBladeBehaviors = new FingerBladeBehavior[3]; // 3根Finger Blade的Behavior

        [Header("手部状态")]
        public bool isActive = false;           // 手部是否激活
        public bool isAttacking = false;        // 手部是否正在攻击

        // 私有变量
        private PlayMakerFSM? handFSM;          // 手部的FSM
        private Transform? playerTransform;     // 玩家Transform

        // 事件引用缓存
        private FsmEvent? _orbitStartEvent;
        private FsmEvent? _shootEvent;

        // 事件
        public System.Action? OnHandActivated;
        public System.Action? OnHandDeactivated;
        public System.Action<int>? OnFingerBladeAttack; // 参数为Finger Blade索引

        private void Start()
        {
            StartCoroutine(InitializeHand());
        }
        private void Update()
        {
            
        }
        /// <summary>
        /// 初始化手部对象
        /// </summary>
        private IEnumerator InitializeHand()
        {
            yield return new WaitForSeconds(0.5f); // 等待游戏对象初始化

            // 获取手部名称
            handName = gameObject.name;
            Log.Info($"初始化手部: {handName}");

            // 查找玩家
            var heroController = FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                playerTransform = heroController.transform;
            }
            else
            {
                Log.Error($"{handName} 未找到玩家Transform");
            }

            // 获取手部的FSM
            handFSM = GetComponent<PlayMakerFSM>();
            if (handFSM != null)
            {
            }
            else
            {
                Log.Warn($"{handName} 未找到FSM");
            }

            // 初始化Finger Blades
            InitializeFingerBlades();

            // 添加环绕攻击状态（在Hand初始化完成后）
            AddOrbitAttackState();
        }

        /// <summary>
        /// 初始化Finger Blades
        /// </summary>
        private void InitializeFingerBlades()
        {
            // 查找手部对象下的所有Finger Blade
            Transform[] handChildren = GetComponentsInChildren<Transform>();
            var fingerBladeTransforms = handChildren.Where(t =>
                t.name == "Finger Blade L" ||
                t.name == "Finger Blade M" ||
                t.name == "Finger Blade R").ToArray();

            int bladeIndex = 0;
            foreach (Transform bladeTransform in fingerBladeTransforms)
            {
                if (bladeIndex >= fingerBlades.Length) break;

                GameObject bladeObj = bladeTransform.gameObject;
                fingerBlades[bladeIndex] = bladeTransform;

                // 添加FingerBladeBehavior组件
                FingerBladeBehavior bladeBehavior = bladeObj.GetComponent<FingerBladeBehavior>();
                if (bladeBehavior == null)
                {
                    bladeBehavior = bladeObj.AddComponent<FingerBladeBehavior>();
                }

                // 初始化FingerBladeBehavior
                bladeBehavior.Initialize(bladeIndex, handName, this);
                fingerBladeBehaviors[bladeIndex] = bladeBehavior;

                // 添加环绕攻击全局转换
                bladeBehavior.AddOrbitGlobalTransition();
                bladeIndex++;
            }

            Log.Info($"{handName} 初始化完成，找到 {bladeIndex} 根Finger Blade");
        }

        /// <summary>
        /// 添加环绕攻击状态和全局转换 - 新版本：两个独立状态
        /// </summary>
        public void AddOrbitAttackState()
        {
            if (handFSM == null) return;
            // 首先注册所有需要的事件
            RegisterHandEvents();

            // 创建第一个状态：Orbit Start（启动环绕，等待SHOOT事件）
            var orbitStartState = new FsmState(handFSM.Fsm)
            {
                Name = "Orbit Start",
                Description = $"{handName} 启动环绕状态",
            };

            // 创建第二个状态：Orbit Shoot（控制三根针间隔发射）
            var orbitShootState = new FsmState(handFSM.Fsm)
            {
                Name = "Orbit Shoot", 
                Description = $"{handName} 环绕发射状态",
            };

            // 添加状态到FSM
            var existingStates = handFSM.FsmStates.ToList();
            existingStates.Add(orbitStartState);
            existingStates.Add(orbitShootState);
            handFSM.Fsm.States = existingStates.ToArray();
            
            // 添加状态动作
            AddOrbitStartActions(orbitStartState);
            AddOrbitShootActions(orbitShootState);
            
            // 先添加全局转换
            AddOrbitAttackGlobalTransition(orbitStartState, orbitShootState);
            
            // 再添加状态转换（此时事件已注册）
            AddOrbitStartTransitions(orbitStartState, orbitShootState);
            AddOrbitShootTransitions(orbitShootState, handFSM!.FsmStates.FirstOrDefault(state => state.Name == "Attack Ready Frame"));

            // 重新链接所有事件引用
            RelinkHandEventReferences();
        }

        private void OnDestroy()
        {
            // 清理对Finger Blade的引用，避免场景返回后残留
            fingerBlades = new Transform[3];
            fingerBladeBehaviors = new FingerBladeBehavior[3];
        }

        /// <summary>
        /// 注册Hand FSM的所有事件
        /// </summary>
        private void RegisterHandEvents()
        {
            if (handFSM == null) return;

            // 创建或获取事件
            _orbitStartEvent = FsmEvent.GetFsmEvent($"ORBIT START {handName}");
            _shootEvent = FsmEvent.GetFsmEvent($"SHOOT {handName}");

            // 将事件添加到FSM的事件列表中
            var existingEvents = handFSM.FsmEvents.ToList();
            
            if (!existingEvents.Contains(_orbitStartEvent))
            {
                existingEvents.Add(_orbitStartEvent);
            }
            if (!existingEvents.Contains(_shootEvent))
            {
                existingEvents.Add(_shootEvent);
            }

            // 使用反射设置FsmEvents
            var fsmType = handFSM.Fsm.GetType();
            var eventsField = fsmType.GetField("events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (eventsField != null)
            {
                eventsField.SetValue(handFSM.Fsm, existingEvents.ToArray());
            }
            else
            {
                Log.Error($"{handName} 未找到events字段");
            }
        }

        /// <summary>
        /// 重新链接Hand FSM事件引用
        /// </summary>
        private void RelinkHandEventReferences()
        {
            if (handFSM == null) return;

            Log.Info($"{handName} 重新链接Hand FSM事件引用");

            // 重新初始化FSM数据，确保所有事件引用正确
            handFSM.Fsm.InitData();
            handFSM.Fsm.InitEvents();

            Log.Info($"{handName} Hand FSM事件引用重新链接完成");
        }

        /// <summary>
        /// 添加环绕攻击全局转换 - 新版本：两个独立状态
        /// </summary>
        private void AddOrbitAttackGlobalTransition(FsmState orbitStartState, FsmState orbitShootState)
        {
            if (_orbitStartEvent == null)
            {
                Log.Error($"{handName} 未找到ORBIT START事件: ORBIT START {handName}");
                return;
            }
            var fsmType = handFSM!.Fsm.GetType();
            // 创建从Idle到Orbit Start的转换
            var idleState = handFSM.FsmStates.FirstOrDefault(state => state.Name == "Idle");
            if (idleState != null)
            {
                var idleToOrbitStartTransition = new FsmTransition
                {
                    FsmEvent = _orbitStartEvent,
                    toState = "Orbit Start",
                    toFsmState = orbitStartState
                };
                var idleTransitions = idleState.Transitions.ToList();
                idleTransitions.Add(idleToOrbitStartTransition);
                idleState.Transitions = idleTransitions.ToArray();
            }

            // 添加全局转换
            var existingTransitions = handFSM.FsmGlobalTransitions.ToList();
            existingTransitions.Add(new FsmTransition
            {
                FsmEvent = _orbitStartEvent,
                toState = "Orbit Start",
                toFsmState = orbitStartState
            });

            // 使用反射设置FsmGlobalTransitions
            var globalTransitionsField = fsmType.GetField("globalTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (globalTransitionsField != null)
            {
                globalTransitionsField.SetValue(handFSM.Fsm, existingTransitions.ToArray());
            }
            else
            {
                Log.Error($"{handName} 未找到globalTransitions字段");
            }

        }

         /// <summary>
        /// 启动环绕攻击序列 - 新版本：发送不同事件给每个Finger Blade
        /// </summary>
        public void StartOrbitAttackSequence()
        {

            // 根据Hand分配不同的角度偏移
            float[] orbitOffsets;
            if (handName == "Hand L")
            {
                orbitOffsets = new float[] { 0f, 120f, 240f }; // Hand L: 0°, 120°, 240°
            }
            else // Hand R
            {
                orbitOffsets = new float[] { 60f, 180f, 300f }; // Hand R: 60°, 180°, 300°
            }

            // 向每个Finger Blade发送不同的事件
            for (int i = 0; i < fingerBladeBehaviors.Length; i++)
            {
                if (fingerBladeBehaviors[i] != null)
                {
                    // 设置每个Finger Blade的环绕偏移角度（使用固定值，不加随机）
                    fingerBladeBehaviors[i].SetOrbitParameters(7f, 200f, orbitOffsets[i]);

                    
                    // 获取Control FSM
                    var controlFSM = fingerBladeBehaviors[i].GetComponents<PlayMakerFSM>()
                        .FirstOrDefault(fsm => fsm.FsmName == "Control");
                    
                    if (controlFSM != null)
                    {
                        // 设置Ready为false
                        var readyVar = controlFSM.FsmVariables.GetFsmBool("Ready");
                        if (readyVar != null)
                        {
                            readyVar.Value = false;
                        }

                        // 发送唯一的事件名给每个Finger Blade（使用全局索引）
                        int globalIndex = handName == "Hand L" ? i : i + 3;
                        string uniqueEventName = $"ORBIT START {handName} Blade {globalIndex}";
                        controlFSM.SendEvent(uniqueEventName);

                    }
                    else
                    {
                        Log.Warn($"{handName} -> Finger Blade {i} 未找到Control FSM");
                    }
                }
            }
        }

        /// <summary>
        /// 开始SHOOT序列 - 每0.5秒触发一个Finger Blade
        /// </summary>
        public void StartShootSequence()
        {
            StartCoroutine(ShootSequence());
        }

        /// <summary>
        /// SHOOT序列协程 - 每0.5秒发送SHOOT事件给一个Finger Blade
        /// </summary>
        private IEnumerator ShootSequence()
        {
            float shootInterval = 0.5f;

            for (int i = 0; i < fingerBladeBehaviors.Length; i++)
            {
                if (i > 0) // 第一个Finger Blade不需要等待
                {
                    yield return new WaitForSeconds(shootInterval);
                }

                if (fingerBladeBehaviors[i] != null)
                {
                    // 获取Control FSM
                    var controlFSM = fingerBladeBehaviors[i].GetComponents<PlayMakerFSM>()
                        .FirstOrDefault(fsm => fsm.FsmName == "Control");
                    
                    if (controlFSM != null)
                    {
                        // 使用全局索引发送SHOOT事件
                        int globalIndex = handName == "Hand L" ? i : i + 3;
                        string shootEventName = $"SHOOT {handName} Blade {globalIndex}";
                        controlFSM.SendEvent(shootEventName);

                    }
                    else
                    {
                        Log.Warn($"{handName} -> Finger Blade {i} 未找到Control FSM");
                    }
                }
            }

        }

        /// <summary>
        /// 添加Orbit Start状态的动作
        /// </summary>
        private void AddOrbitStartActions(FsmState orbitStartState)
        {
            if (_shootEvent == null)
            {
                Log.Error($"{handName} 未找到SHOOT事件: SHOOT {handName}");
                return;
            }
            // 动作1：启动环绕攻击序列
            var startOrbitAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartOrbitAttackSequence"){Value = "StartOrbitAttackSequence"},
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // 动作2：等待SHOOT事件或超时6秒
            var waitForShootAction = new Wait
            {
                time = new FsmFloat(6f), // 超时6秒
                finishEvent = _shootEvent 
            };

            orbitStartState.Actions = new FsmStateAction[] { startOrbitAction, waitForShootAction };
        }

        /// <summary>
        /// 添加Orbit Shoot状态的动作
        /// </summary>
        private void AddOrbitShootActions(FsmState orbitShootState)
        {
            // 动作1：开始SHOOT序列
            var startShootAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartShootSequence"){Value = "StartShootSequence"},
                parameters = new FsmVar[0],
                everyFrame = false
            };

            var waitFinishAction = new Wait
            {
                time = new FsmFloat(0.2f),
                finishEvent = FsmEvent.Finished
            };

            orbitShootState.Actions = new FsmStateAction[] { startShootAction, waitFinishAction };
        }

        /// <summary>
        /// 添加Orbit Start状态的转换
        /// </summary>
        private void AddOrbitStartTransitions(FsmState orbitStartState, FsmState orbitShootState)
        {
            // 从Orbit Start到Orbit Shoot的转换（通过SHOOT事件）
            if (_shootEvent == null)
            {
                Log.Error($"{handName} 未找到SHOOT事件: SHOOT {handName}");
                return;
            }

            var shootTransition = new FsmTransition
            {
                FsmEvent = _shootEvent,
                toState = "Orbit Shoot",
                toFsmState = orbitShootState
            };

            orbitStartState.Transitions = new FsmTransition[] { shootTransition };
        }

        /// <summary>
        /// 添加Orbit Shoot状态的转换
        /// </summary>
        private void AddOrbitShootTransitions(FsmState orbitShootState, FsmState attackReadyFrameState)
        {
            // 发射完成后回到Attack Ready Frame状态
            var finishedTransition = new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Attack Ready Frame",
                toFsmState = attackReadyFrameState 
            };

            orbitShootState.Transitions = new FsmTransition[] { finishedTransition };
        }

    }
}
