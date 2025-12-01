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

        [Header("环绕攻击配置")]
        public float orbitRotationDirection = 1f;  // 环绕旋转方向：1=顺时针，-1=逆时针
        public float bladeShootInterval = 0.5f;    // 单个指针发射间隔

        // 私有变量
        private PlayMakerFSM? handFSM;          // 手部的FSM
        private Transform? playerTransform;     // 玩家Transform

        // 事件引用缓存
        private FsmEvent? _orbitStartEvent;
        private FsmEvent? _shootEvent;
        private void Start()
        {
            StartCoroutine(InitializeHand());
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                LogHandFSMInfo();
            }
        }
        private void LogHandFSMInfo()
        {
            if (handFSM != null)
            {
                Log.Info($"=== Attack Control FSM 信息 ===");
                Log.Info($"FSM名称: {handFSM.FsmName}");
                Log.Info($"当前状态: {handFSM.ActiveStateName}");
                FsmAnalyzer.WriteFsmReport(handFSM, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\handFSM.txt");
            }
            else
            {
                Log.Warn($"手部FSM未找到");
            }
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

            // 修改 Stomp 和 Swipe 攻击状态（使用新的自定义状态链）
            ModifyStompState_New();
            ModifySwipeStates_New();
            ModifyOriginalSwipeStates(); // 修改原版 Swipe 状态，添加追踪参数设置
            handFSM.FsmVariables.Init();
            handFSM.Fsm.InitData();
            handFSM.Fsm.InitEvents();
            yield break;
        }
        /// <summary>
        /// 添加自定义 Stomp 状态（垂直和斜向）
        /// </summary>
        private void ModifyStompState_New()
        {
            if (handFSM == null) return;

            // 创建三个Stomp状态
            CreateStompState("Custom Hand Stomp Center", "CUSTOM_STOMP_CENTER", 90f, new float[] { -6f, 0f, 6f });
            CreateStompState("Custom Hand Stomp Left", "CUSTOM_STOMP_LEFT", 135f, new float[] { -6f, 0f, 6f });
            CreateStompState("Custom Hand Stomp Right", "CUSTOM_STOMP_RIGHT", 45f, new float[] { -6f, 0f, 6f });

            // 创建决策状态：随机选择 Left 或 Right
            string decisionStateName = "Custom Stomp Decision";
            var decisionState = new FsmState(handFSM.Fsm) { Name = decisionStateName };
            var decisionActions = new List<FsmStateAction>();

            var randomBool = handFSM.FsmVariables.GetFsmBool("Stomp Random Bool");
            if (randomBool == null)
            {
                randomBool = new FsmBool("Stomp Random Bool");
                var boolVars = handFSM.FsmVariables.BoolVariables.ToList();
                boolVars.Add(randomBool);
                handFSM.FsmVariables.BoolVariables = boolVars.ToArray();
            }

            // 注册决策事件到 FSM
            var events = handFSM.Fsm.Events.ToList();
            var evtL = FsmEvent.GetFsmEvent("CUSTOM_STOMP_L_DECISION");
            var evtR = FsmEvent.GetFsmEvent("CUSTOM_STOMP_R_DECISION");
            if (!events.Contains(evtL)) events.Add(evtL);
            if (!events.Contains(evtR)) events.Add(evtR);
            handFSM.Fsm.Events = events.ToArray();

            decisionActions.Add(new RandomBool { storeResult = randomBool });

            // 设置Ready变量为false
            var readyVar = handFSM.FsmVariables.GetFsmBool("Ready");
            if (readyVar != null)
            {
                decisionActions.Add(new SetBoolValue { boolVariable = readyVar, boolValue = false, everyFrame = false });
            }

            decisionActions.Add(new BoolTest
            {
                boolVariable = randomBool,
                isTrue = FsmEvent.GetFsmEvent("CUSTOM_STOMP_L_DECISION"), // 临时事件名，只要不重复即可
                isFalse = FsmEvent.GetFsmEvent("CUSTOM_STOMP_R_DECISION"),
                everyFrame = false
            });
            decisionState.Actions = decisionActions.ToArray();

            // 添加转换
            var leftState = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Custom Hand Stomp Left");
            var rightState = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Custom Hand Stomp Right");

            decisionState.Transitions = new FsmTransition[]
            {
                new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("CUSTOM_STOMP_L_DECISION"), toState = leftState.Name, toFsmState = leftState },
                new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("CUSTOM_STOMP_R_DECISION"), toState = rightState.Name, toFsmState = rightState }
            };

            // 添加决策状态到 FSM
            var states = handFSM.FsmStates.ToList();
            states.Add(decisionState);
            handFSM.Fsm.States = states.ToArray();

            // 修改 STOMP 全局转换指向 Custom Stomp Decision
            var globalTrans = handFSM.FsmGlobalTransitions.ToList();
            var stompTran = globalTrans.FirstOrDefault(t => t.FsmEvent.Name == "STOMP");
            if (stompTran != null)
            {
                stompTran.toState = decisionStateName;
                stompTran.toFsmState = decisionState;
            }
            handFSM.Fsm.GlobalTransitions = globalTrans.ToArray();


        }

        /// <summary>
        /// 创建 Stomp 状态（通用方法）
        /// </summary>
        private void CreateStompState(string stateName, string eventName, float rotation, float[] xOffsets)
        {
            if (handFSM == null) return;

            var customStompState = new FsmState(handFSM.Fsm) { Name = stateName };
            var actions = new List<FsmStateAction>();

            var heroXVar = handFSM.FsmVariables.GetFsmFloat("Hero X");
            var heroYVar = handFSM.FsmVariables.GetFsmFloat("Hero Y");

            var getHeroPos = new GetPosition
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = HeroController.instance.gameObject } },
                vector = new FsmVector3(),
                x = heroXVar,
                y = heroYVar,
                z = new FsmFloat(),
                space = Space.World,
                everyFrame = false
            };
            actions.Add(getHeroPos);

            // 限制Hero X范围（参考原版：22-55）
            var clampHeroX = new FloatClamp
            {
                floatVariable = heroXVar,
                minValue = new FsmFloat(22f),
                maxValue = new FsmFloat(55f),
                everyFrame = false
            };
            actions.Add(clampHeroX);

            for (int i = 0; i < fingerBlades.Length; i++)
            {
                if (fingerBlades[i] == null) continue;
                string bladeName = fingerBlades[i].name;
                float offset = 0f;
                if (bladeName.Contains("Blade L")) offset = xOffsets[0];
                else if (bladeName.Contains("Blade M")) offset = xOffsets[1];
                else if (bladeName.Contains("Blade R")) offset = xOffsets[2];

                var attackXVar = handFSM.FsmVariables.GetFsmFloat($"Attack X {i}");
                var setHeroXToVar = new SetFloatValue { floatVariable = attackXVar, floatValue = heroXVar, everyFrame = false };
                var addXOffset = new FloatAdd { floatVariable = attackXVar, add = new FsmFloat(offset), everyFrame = false };

                actions.Add(setHeroXToVar);
                actions.Add(addXOffset);

                var setBladeX = new SetFsmFloat { gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject } }, fsmName = new FsmString("Control") { Value = "Control" }, variableName = new FsmString("Attack X") { Value = "Attack X" }, setValue = attackXVar, everyFrame = false };
                var setBladeRot = new SetFsmFloat { gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject } }, fsmName = new FsmString("Control") { Value = "Control" }, variableName = new FsmString("Attack Rotation") { Value = "Attack Rotation" }, setValue = new FsmFloat(rotation), everyFrame = false };

                actions.Add(setBladeX);
                actions.Add(setBladeRot);

                var sendEvent = new SendEventByName
                {
                    eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObject,
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject }
                        }
                    },
                    sendEvent = eventName,
                    delay = new FsmFloat(0f),
                    everyFrame = false
                };
                actions.Add(sendEvent);
            }

            var waitAction = new Wait { time = 2.0f, finishEvent = FsmEvent.Finished };
            actions.Add(waitAction);

            customStompState.Actions = actions.ToArray();

            var states = handFSM.FsmStates.ToList();
            states.Add(customStompState);
            handFSM.Fsm.States = states.ToArray();

            var attackReadyState = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Attack Ready Frame");
            customStompState.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = "Attack Ready Frame", toFsmState = attackReadyState } };
        }

        /// <summary>
        /// 添加自定义 Swipe 状态（区分左右方向）
        /// </summary>
        private void ModifySwipeStates_New()
        {
            if (handFSM == null) return;

            // 1. 先创建 Swipe L 和 Swipe R 状态（必须先创建，因为Swipe Dir需要引用它们）
            // 注意：L/R 指的是攻击方向，不是出现位置
            // Swipe L = 向左攻击 = 从右边出现，朝向左
            // Swipe R = 向右攻击 = 从左边出现，朝向右
            CreateSwipeState("Custom Hand Swipe L", "CUSTOM_SWIPE_L", 11.5f, 180f, 1f); // 从右侧出现，朝向左，向左攻击
            CreateSwipeState("Custom Hand Swipe R", "CUSTOM_SWIPE_R", -11.5f, 0f, -1f); // 从左侧出现，朝向右，向右攻击

            // 2. 创建 Custom Hand Swipe Dir 状态（判断方向，转换到上面创建的状态）
            CreateSwipeDirState();

            // 3. 修改 SWIPE 全局转换指向 Swipe Dir
            var globalTrans = handFSM.FsmGlobalTransitions.ToList();
            var swipeTran = globalTrans.FirstOrDefault(t => t.FsmEvent.Name == "SWIPE");
            if (swipeTran != null)
            {
                var swipeDirState = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Custom Hand Swipe Dir");
                swipeTran.toState = "Custom Hand Swipe Dir";
                swipeTran.toFsmState = swipeDirState;
            }
            handFSM.Fsm.GlobalTransitions = globalTrans.ToArray();
        }

        /// <summary>
        /// 创建 Swipe Dir 状态（根据玩家位置判断方向）
        /// </summary>
        private void CreateSwipeDirState()
        {
            if (handFSM == null) return;

            var swipeDirState = new FsmState(handFSM.Fsm) { Name = "Custom Hand Swipe Dir" };
            var actions = new List<FsmStateAction>();

            // 设置Ready变量为false
            var readyVar = handFSM.FsmVariables.GetFsmBool("Ready");
            if (readyVar != null)
            {
                actions.Add(new SetBoolValue { boolVariable = readyVar, boolValue = false, everyFrame = false });
            }

            // 调用 SetFingerBladePhase2Parameters 方法来设置追踪参数
            var callMethod = new CallMethod
            {
                behaviour = this,
                methodName = "SetFingerBladePhase2Parameters",
                parameters = new FsmVar[0],
                everyFrame = false
            };
            actions.Add(callMethod);

            // 获取玩家位置
            var heroXVar = handFSM.FsmVariables.GetFsmFloat("Hero X");
            var getHeroPos = new GetPosition
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = HeroController.instance.gameObject } },
                vector = new FsmVector3(),
                x = heroXVar,
                y = new FsmFloat(),
                z = new FsmFloat(),
                space = Space.World,
                everyFrame = false
            };
            actions.Add(getHeroPos);

            // 注册自定义事件（避免与原版L/R事件冲突）
            var swipeDirLEvent = FsmEvent.GetFsmEvent("SWIPE_DIR_L");
            var swipeDirREvent = FsmEvent.GetFsmEvent("SWIPE_DIR_R");
            var nullEvent = FsmEvent.GetFsmEvent("NULL");
            var events = handFSM.Fsm.Events.ToList();
            if (!events.Contains(swipeDirLEvent)) events.Add(swipeDirLEvent);
            if (!events.Contains(swipeDirREvent)) events.Add(swipeDirREvent);
            handFSM.Fsm.Events = events.ToArray();

            // CheckXPosition: 大于47发送SWIPE_DIR_R事件
            var checkRight = new CheckXPosition
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = HeroController.instance.gameObject } },
                compareTo = new FsmFloat(47f),
                compareToOffset = new FsmFloat(0f),
                tolerance = new FsmFloat(0f),
                equal = nullEvent,
                equalBool = new FsmBool(false),
                greaterThan = swipeDirREvent,
                greaterThanBool = new FsmBool(false),
                lessThan = nullEvent,
                lessThanBool = new FsmBool(false),
                everyFrame = false,
                activeBool = new FsmBool(false)
            };
            actions.Add(checkRight);

            // CheckXPosition: 小于33发送SWIPE_DIR_L事件
            var checkLeft = new CheckXPosition
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = HeroController.instance.gameObject } },
                compareTo = new FsmFloat(33f),
                compareToOffset = new FsmFloat(0f),
                tolerance = new FsmFloat(0f),
                equal = nullEvent,
                equalBool = new FsmBool(false),
                greaterThan = nullEvent,
                greaterThanBool = new FsmBool(false),
                lessThan = swipeDirLEvent,
                lessThanBool = new FsmBool(false),
                everyFrame = false,
                activeBool = new FsmBool(false)
            };
            actions.Add(checkLeft);

            // 其他情况随机选择
            var randomEvent = new SendRandomEvent
            {
                events = new FsmEvent[] { swipeDirLEvent, swipeDirREvent },
                weights = new FsmFloat[] { new FsmFloat(1f), new FsmFloat(1f) },
                delay = new FsmFloat(0f)
            };
            actions.Add(randomEvent);

            swipeDirState.Actions = actions.ToArray();

            // 添加转换到 Swipe L 和 Swipe R
            var swipeLState = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Custom Hand Swipe L");
            var swipeRState = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Custom Hand Swipe R");
            swipeDirState.Transitions = new FsmTransition[]
            {
                new FsmTransition { FsmEvent = swipeDirLEvent, toState = "Custom Hand Swipe L", toFsmState = swipeLState },
                new FsmTransition { FsmEvent = swipeDirREvent, toState = "Custom Hand Swipe R", toFsmState = swipeRState }
            };

            var states = handFSM.FsmStates.ToList();
            states.Add(swipeDirState);
            handFSM.Fsm.States = states.ToArray();
        }

        /// <summary>
        /// 创建 Swipe 状态（通用方法）
        /// </summary>
        private void CreateSwipeState(string stateName, string eventName, float xOffset, float rotation, float yScale)
        {
            if (handFSM == null) return;

            var customSwipeState = new FsmState(handFSM.Fsm) { Name = stateName };
            var actions = new List<FsmStateAction>();

            var heroXVar = handFSM.FsmVariables.GetFsmFloat("Hero X");
            var heroYVar = handFSM.FsmVariables.GetFsmFloat("Hero Y");

            var getHeroPos = new GetPosition
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = HeroController.instance.gameObject } },
                vector = new FsmVector3(),
                x = heroXVar,
                y = heroYVar,
                z = new FsmFloat(),
                space = Space.World,
                everyFrame = false
            };
            actions.Add(getHeroPos);

            float[] yOffsets = { 2f, 0f, -2f }; // L(Top), M(Mid), R(Bot) - 相对于玩家的偏移

            for (int i = 0; i < fingerBlades.Length; i++)
            {
                if (fingerBlades[i] == null) continue;
                string bladeName = fingerBlades[i].name;
                float yOffset = 0f;
                if (bladeName.Contains("Blade L")) yOffset = yOffsets[0];
                else if (bladeName.Contains("Blade M")) yOffset = yOffsets[1];
                else if (bladeName.Contains("Blade R")) yOffset = yOffsets[2];

                // 设置 Attack X（玩家X + 固定偏移，限制在场景范围内）
                var attackXVar = handFSM.FsmVariables.GetFsmFloat($"Attack X {i}");
                var setAttackX = new SetFloatValue { floatVariable = attackXVar, floatValue = heroXVar, everyFrame = false };
                var addXOffset = new FloatAdd { floatVariable = attackXVar, add = new FsmFloat(xOffset), everyFrame = false };
                var clampAttackX = new FloatClamp { floatVariable = attackXVar, minValue = new FsmFloat(23f), maxValue = new FsmFloat(55f), everyFrame = false };

                actions.Add(setAttackX);
                actions.Add(addXOffset);
                actions.Add(clampAttackX);

                // 设置 Attack Y（玩家Y + 相对偏移）
                var attackYVar = handFSM.FsmVariables.GetFsmFloat($"Attack Y {i}");
                var setAttackY = new SetFloatValue { floatVariable = attackYVar, floatValue = heroYVar, everyFrame = false };
                var addYOffset = new FloatAdd { floatVariable = attackYVar, add = new FsmFloat(yOffset), everyFrame = false };

                actions.Add(setAttackY);
                actions.Add(addYOffset);

                // 传递参数到 Finger Blade
                var setBladeX = new SetFsmFloat { gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject } }, fsmName = new FsmString("Control") { Value = "Control" }, variableName = new FsmString("Attack X") { Value = "Attack X" }, setValue = attackXVar, everyFrame = false };
                var setBladeY = new SetFsmFloat { gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject } }, fsmName = new FsmString("Control") { Value = "Control" }, variableName = new FsmString("Attack Y") { Value = "Attack Y" }, setValue = attackYVar, everyFrame = false };
                var setBladeRot = new SetFsmFloat { gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject } }, fsmName = new FsmString("Control") { Value = "Control" }, variableName = new FsmString("Attack Rotation") { Value = "Attack Rotation" }, setValue = new FsmFloat(rotation), everyFrame = false };
                var setBladeYScale = new SetFsmFloat { gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject } }, fsmName = new FsmString("Control") { Value = "Control" }, variableName = new FsmString("Attack Y Scale") { Value = "Attack Y Scale" }, setValue = new FsmFloat(yScale), everyFrame = false };

                actions.Add(setBladeX);
                actions.Add(setBladeY);
                actions.Add(setBladeRot);
                actions.Add(setBladeYScale);

                var sendEvent = new SendEventByName
                {
                    eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObject,
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            gameObject = new FsmGameObject { Value = fingerBlades[i].gameObject }
                        }
                    },
                    sendEvent = eventName,
                    delay = new FsmFloat(0f),
                    everyFrame = false
                };
                actions.Add(sendEvent);
            }

            var waitAction = new Wait { time = 2.5f, finishEvent = FsmEvent.Finished };
            actions.Add(waitAction);

            customSwipeState.Actions = actions.ToArray();

            var states = handFSM.FsmStates.ToList();
            states.Add(customSwipeState);
            handFSM.Fsm.States = states.ToArray();

            var attackReadyState = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Attack Ready Frame");
            customSwipeState.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = "Attack Ready Frame", toFsmState = attackReadyState } };
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
            var existingEvents = handFSM.Fsm.Events.ToList();

            if (!existingEvents.Contains(_orbitStartEvent))
            {
                existingEvents.Add(_orbitStartEvent);
            }
            if (!existingEvents.Contains(_shootEvent))
            {
                existingEvents.Add(_shootEvent);
            }

            handFSM.Fsm.Events = existingEvents.ToArray();
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
            // 创建从Idle到Orbit Start的转换
            var idleState = handFSM!.FsmStates.FirstOrDefault(state => state.Name == "Idle");
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
            var existingTransitions = handFSM.Fsm.GlobalTransitions.ToList();
            existingTransitions.Add(new FsmTransition
            {
                FsmEvent = _orbitStartEvent,
                toState = "Orbit Start",
                toFsmState = orbitStartState
            });

            handFSM.Fsm.GlobalTransitions = existingTransitions.ToArray();

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
                    // 获取Control FSM
                    var controlFSM = fingerBladeBehaviors[i].GetComponents<PlayMakerFSM>()
                        .FirstOrDefault(fsm => fsm.FsmName == "Control");

                    if (controlFSM != null)
                    {
                        // 设置每个Finger Blade的环绕参数（直接通过FSM变量）
                        // 速度通过方向调整：正数=顺时针，负数=逆时针
                        float adjustedSpeed = 200f * orbitRotationDirection;
                        // ⚠️ 偏移角度也要根据旋转方向调整，防止贴图和攻击位置不符
                        float adjustedOffset = orbitOffsets[i] * orbitRotationDirection;

                        // 直接设置FSM变量，确保稳定性
                        var radiusVar = controlFSM.FsmVariables.GetFsmFloat("OrbitRadius");
                        if (radiusVar != null) radiusVar.Value = 7f;

                        var speedVar = controlFSM.FsmVariables.GetFsmFloat("OrbitSpeed");
                        if (speedVar != null) speedVar.Value = adjustedSpeed;

                        var offsetVar = controlFSM.FsmVariables.GetFsmFloat("OrbitOffset");
                        if (offsetVar != null) offsetVar.Value = adjustedOffset;

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

                        Log.Info($"{handName} Finger Blade {i}: Radius=7, Speed={adjustedSpeed}, Offset={adjustedOffset}");
                    }
                    else
                    {
                        Log.Warn($"{handName} -> Finger Blade {i} 未找到Control FSM");
                    }
                }
            }
        }

        /// <summary>
        /// 开始SHOOT序列 - 按配置间隔触发一个Finger Blade
        /// </summary>
        public void StartShootSequence()
        {
            StartCoroutine(ShootSequence());
        }

        /// <summary>
        /// 设置环绕政击参数（供外部调用）
        /// </summary>
        public void SetOrbitAttackConfig(float rotationDirection, float shootInterval)
        {
            orbitRotationDirection = rotationDirection;
            bladeShootInterval = shootInterval;
            Log.Info($"{handName} 设置环绕攻击参数: 方向={rotationDirection}, 间隔={shootInterval}秒");
        }

        /// <summary>
        /// SHOOT序列协程 - 按配置间隔发送SHOOT事件给一个Finger Blade
        /// </summary>
        private IEnumerator ShootSequence()
        {
            for (int i = 0; i < fingerBladeBehaviors.Length; i++)
            {
                if (i > 0) // 第一个Finger Blade不需要等待
                {
                    yield return new WaitForSeconds(bladeShootInterval);
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
                methodName = new FsmString("StartOrbitAttackSequence") { Value = "StartOrbitAttackSequence" },
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
                methodName = new FsmString("StartShootSequence") { Value = "StartShootSequence" },
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

        /// <summary>
        /// 设置 Finger Blade 的追踪参数（Track Time）和二阶段变量（Special Attack）
        /// 无参数版本，自动查找 Phase Control FSM
        /// </summary>
        public void SetFingerBladePhase2Parameters()
        {
            // 自动查找 Phase Control FSM
            var bossObject = GameObject.Find("Silk Boss");
            if (bossObject == null)
            {
                Log.Warn($"{handName} 未找到 Silk Boss 对象");
                return;
            }

            var phaseControlFSM = FSMUtility.LocateMyFSM(bossObject, "Phase Control");
            if (phaseControlFSM == null)
            {
                Log.Warn($"{handName} 未找到 Phase Control FSM，无法设置追踪参数");
                return;
            }

            // 获取 Phase Control FSM 的 Special Attack 变量
            var specialAttackVar = phaseControlFSM.FsmVariables.GetFsmBool("Special Attack");
            if (specialAttackVar == null)
            {
                Log.Warn($"{handName} 未找到 Phase Control FSM 的 Special Attack 变量");
                return;
            }

            // 为每根 Finger Blade 设置追踪时间（随机分配）
            List<float> trackTimes = new List<float> { 1.2f + UnityEngine.Random.Range(-0.1f, 0.1f), 1.55f + UnityEngine.Random.Range(-0.1f, 0.1f), 1.9f + UnityEngine.Random.Range(-0.1f, 0.1f) };

            // 随机打乱追踪时间列表
            for (int i = trackTimes.Count - 1; i > 0; i--)
            {
                int randomIndex = UnityEngine.Random.Range(0, i + 1);
                float temp = trackTimes[i];
                trackTimes[i] = trackTimes[randomIndex];
                trackTimes[randomIndex] = temp;
            }

            for (int i = 0; i < fingerBladeBehaviors.Length; i++)
            {
                var bladeBehavior = fingerBladeBehaviors[i];
                if (bladeBehavior == null) continue;

                // 获取 Finger Blade 的 Control FSM
                var controlFSM = bladeBehavior.GetComponent<PlayMakerFSM>();
                if (controlFSM == null || controlFSM.FsmName != "Control") continue;

                // 设置 Track Time 变量（随机分配）
                var trackTimeVar = controlFSM.FsmVariables.GetFsmFloat("Track Time");
                if (trackTimeVar != null && i < trackTimes.Count)
                {
                    trackTimeVar.Value = trackTimes[i];
                    Log.Info($"{handName} Finger Blade {i} Track Time 随机设置为 {trackTimes[i]}s");
                }
                else
                {
                    Log.Warn($"{handName} Finger Blade {i} 未找到 Track Time 变量");
                }

                // 获取 Finger Blade 的 Special Attack 变量并同步
                var bladeSpecialAttackVar = controlFSM.FsmVariables.GetFsmBool("Special Attack");
                if (bladeSpecialAttackVar != null)
                {
                    bladeSpecialAttackVar.Value = specialAttackVar.Value;
                    Log.Info($"{handName} Finger Blade {i} Special Attack 同步为 {specialAttackVar.Value}");
                }
            }
        }

        /// <summary>
        /// 修改原版 Swipe 状态，添加追踪参数设置
        /// </summary>
        private void ModifyOriginalSwipeStates()
        {
            if (handFSM == null) return;
            var SwipeDir = handFSM.FsmStates.FirstOrDefault(s => s.Name == "Swipe Dir");
            if (SwipeDir != null)
            {
                var actions = SwipeDir.Actions.ToList();
                var callMethod = new CallMethod
                {
                    behaviour = this,
                    methodName = "SetFingerBladePhase2Parameters",
                    parameters = new FsmVar[0],
                    everyFrame = false
                };
                actions.Insert(0, callMethod); // 在开头插入
                SwipeDir.Actions = actions.ToArray();
                Log.Info($"{handName} Set Swipe L Q 状态已添加追踪参数设置");
            }
        }
    }
}