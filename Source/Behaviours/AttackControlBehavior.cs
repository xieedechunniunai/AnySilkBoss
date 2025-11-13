using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using System;
using System.Reflection;
using AnySilkBoss.Source.Managers;
namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// Attack Control FSM修改器
    /// 修改Silk Boss的Attack Control FSM，添加环绕攻击状态
    /// 使用新的模块化架构：Hand L/R -> Finger Blades
    /// </summary>
    internal class AttackControlBehavior : MonoBehaviour
    {
        [Header("FSM配置")]
        public string fsmName = "Attack Control";

        [Header("状态配置")]
        public string handPtnChoiceState = "Hand Ptn Choice";
        public string newOrbitAttackState = "Orbit Attack";
        public string waitForHandsReadyState = "Wait For Hands Ready";

        [Header("手部控制")]
        public GameObject? handL;
        public GameObject? handR;
        public HandControlBehavior? handLBehavior;
        public HandControlBehavior? handRBehavior;

        // FSM引用
        private PlayMakerFSM? _attackControlFsm;

        // 状态引用
        private FsmState? _handPtnChoiceState;
        private FsmState? _orbitAttackState;
        private FsmState? _waitForHandsReadyState;
        // 用于存储第二个Hand的信息
        private string _secondHandName = "";
        private GameObject? _bossScene;
        private GameObject? _strandPatterns;
        private GameObject? _secondHandObject;

        // 事件引用缓存
        private FsmEvent? _orbitAttackEvent;
        private FsmEvent? _orbitStartHandLEvent;
        private FsmEvent? _orbitStartHandREvent;

        private FsmEvent? _nullEvent;

        // 丝球环绕攻击相关
        private FsmEvent? _silkBallAttackEvent;
        private Managers.SilkBallManager? _silkBallManager;
        private List<GameObject> _activeSilkBalls = new List<GameObject>();
        private Coroutine? _silkBallSummonCoroutine;

        // 丝球攻击状态引用
        private FsmState? _silkBallPrepareState;
        private FsmState? _silkBallCastState;
        private FsmState? _silkBallLiftState;
        private FsmState? _silkBallAnticState;
        private FsmState? _silkBallReleaseState;
        private FsmState? _silkBallEndState;
        private FsmState? _silkBallRecoverState;

        // 移动丝球攻击状态引用
        private FsmState? _silkBallDashPrepareState;
        private FsmState? _silkBallDashEndState;

        // BossControl FSM引用（用于通信）
        private PlayMakerFSM? _bossControlFsm;

        // BossBehavior引用（用于访问Route Point变量）
        private BossBehavior? _bossBehavior;

        // 移动丝球相关变量（AttackControl中）
        private FsmBool? _isGeneratingSilkBall;  // 是否正在生成丝球
        private FsmFloat? _totalDistanceTraveled; // 累计移动距离
        private FsmVector2? _lastBallPosition;    // 上次生成丝球的位置

        // 移动丝球相关事件
        private FsmEvent? _silkBallStaticEvent;
        private FsmEvent? _silkBallDashEvent;
        private FsmEvent? _silkBallDashStartEvent;
        private FsmEvent? _silkBallDashEndEvent;

        // 眩晕中断相关事件
        private FsmEvent? _silkBallInterruptEvent;
        private FsmEvent? _silkBallRecoverEvent;

        // 丝球释放时的冲击波和音效动作缓存
        private StartRoarEmitter? _cachedRoarEmitter;
        private PlayAudioEventRandom? _cachedPlayRoarAudio;

        // 坐标常量
        private static readonly Vector3 POS_LEFT_DOWN = new Vector3(25f, 137f, 0f);
        private static readonly Vector3 POS_LEFT_UP = new Vector3(25f, 142.5f, 0f);
        private static readonly Vector3 POS_MIDDLE_UP = new Vector3(39.5f, 142.5f, 0f);
        private static readonly Vector3 POS_MIDDLE_DOWN = new Vector3(39.5f, 137f, 0f);
        private static readonly Vector3 POS_RIGHT_UP = new Vector3(51.5f, 142.5f, 0f);
        private static readonly Vector3 POS_RIGHT_DOWN = new Vector3(51.5f, 137f, 0f);

        // 区域判断阈值
        private const float ZONE_LEFT_MAX = 31f;
        private const float ZONE_RIGHT_MIN = 46f;

        // Boss区域枚举
        private enum BossZone { Left, Middle, Right }

        #region 整体/全局方法
        private void Start()
        {
            StartCoroutine(DelayedSetup());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                LogAttackControlFSMInfo();
            }


            // 移动丝球生成检测
            if (_isGeneratingSilkBall != null && _isGeneratingSilkBall.Value)
            {
                CheckAndSpawnSilkBall();
            }
        }


        /// <summary>
        /// 查看Attack Control FSM的所有状态、跳转和全局跳转
        /// </summary>
        public void LogAttackControlFSMInfo()
        {
            if (_attackControlFsm == null)
            {
                Log.Warn("Attack Control FSM未找到");
                return;
            }

            Log.Info($"=== Attack Control FSM 信息 ===");
            Log.Info($"FSM名称: {_attackControlFsm.FsmName}");
            Log.Info($"当前状态: {_attackControlFsm.ActiveStateName}");
            FsmAnalyzer.WriteFsmReport(_attackControlFsm, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\暂存\\_attackControlFsm.txt");
        }

        private IEnumerator DelayedSetup()
        {
            yield return null; // 等待一帧，确保FSM已初始化

            // 等待FSM初始化
            yield return new WaitWhile(() => FSMUtility.LocateMyFSM(gameObject, fsmName) == null);

            GetComponents();
            // 获取BossControl FSM引用
            GetBossControlFSM();
            // 首先注册所有需要的事件
            RegisterAttackControlEvents();
            ModifyAttackControlFSM();



            Log.Info("Attack Control 初始化完成（Hand 的环绕攻击状态将在其自身初始化时添加）");
        }


        /// <summary>
        /// 获取必要的组件
        /// </summary>
        private void GetComponents()
        {
            _attackControlFsm = FSMUtility.LocateMyFSM(gameObject, fsmName);

            if (_attackControlFsm == null)
            {
                Log.Error($"未找到 {fsmName} FSM");
                return;
            }
            _bossScene = transform.GetParent().gameObject;
            if (_bossScene == null)
            {
                Log.Error($"未找到bossScene");
                return;
            }
            _strandPatterns = _bossScene.transform.Find("Strand Patterns").gameObject;
            // 初始化移动丝球相关变量
            InitializeSilkBallDashVariables();

            // 获取SilkBallManager
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _silkBallManager = managerObj.GetComponent<Managers.SilkBallManager>();
                if (_silkBallManager != null)
                {
                }
                else
                {
                    Log.Warn("未找到 SilkBallManager 组件");
                }
            }
            else
            {
                Log.Warn("未找到 AnySilkBossManager GameObject");
            }

            // 初始化手部Behavior组件
            InitializeHandBehaviors();

            // 初始化并缓存丝球释放时的冲击波和音效动作
            InitializeSilkBallReleaseActions();
        }

        /// <summary>
        /// 初始化并缓存丝球释放时的冲击波和音效动作
        /// </summary>
        private void InitializeSilkBallReleaseActions()
        {
            if (_attackControlFsm == null)
            {
                Log.Warn("无法初始化丝球释放动作：_attackControlFsm为null");
                return;
            }

            // 克隆并缓存冲击波效果动作
            _cachedRoarEmitter = CloneAction<StartRoarEmitter>("Roar");
            if (_cachedRoarEmitter != null)
            {
                _cachedRoarEmitter.Fsm = _attackControlFsm.Fsm;
                _cachedRoarEmitter.Owner = _attackControlFsm.gameObject;
            }

            // 克隆并缓存尖叫音效动作
            _cachedPlayRoarAudio = CloneAction<PlayAudioEventRandom>("Roar");
            if (_cachedPlayRoarAudio != null)
            {
                _cachedPlayRoarAudio.Fsm = _attackControlFsm.Fsm;
                _cachedPlayRoarAudio.Owner = _attackControlFsm.gameObject;
            }
        }

        /// <summary>
        /// 获取BossControl FSM引用和BossBehavior引用
        /// </summary>
        private void GetBossControlFSM()
        {
            _bossControlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_bossControlFsm == null)
            {
                Log.Error("未找到 BossControl FSM");
            }


            // 获取BossBehavior组件
            _bossBehavior = GetComponent<BossBehavior>();
            if (_bossBehavior == null)
            {
                Log.Error("未找到 BossBehavior 组件");
            }

        }


        /// <summary>
        /// 修改Attack Control FSM
        /// </summary>
        private void ModifyAttackControlFSM()
        {
            if (_attackControlFsm == null) return;

            Log.Info("开始修改Attack Control FSM");

            // 查找Hand Ptn Choice状态
            _handPtnChoiceState = _attackControlFsm!.FsmStates.FirstOrDefault(state => state.Name == handPtnChoiceState);
            if (_handPtnChoiceState == null)
            {
                Log.Error($"未找到状态: {handPtnChoiceState}");
                return;
            }


            // 查找Wait For Hands Ready状态
            _waitForHandsReadyState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == waitForHandsReadyState);
            if (_waitForHandsReadyState == null)
            {
                Log.Error($"未找到状态: {waitForHandsReadyState}");
                return;
            }

            // 创建新的环绕攻击状态
            CreateOrbitAttackState();

            // 创建丝球环绕攻击状态链
            CreateSilkBallAttackStates();

            // 创建爬升阶段攻击状态链
            CreateClimbPhaseAttackStates();

            // 修改SendRandomEventV4动作，添加新的事件
            ModifySendRandomEventAction();

            // 修改Attack Choice，添加丝球攻击
            ModifyAttackChoiceForSilkBall();
            // 新增：初始化后处理原版攻击Pattern补丁
            PatchOriginalAttackPatterns();
            // 重新链接所有事件引用
            RelinkAllEventReferences();

            // // 针对Boss眩晕添加中断处理
            // AddStunHandlingHooks();

            Log.Info("Attack Control FSM修改完成");
        }
        /// <summary>
        /// 注册Attack Control FSM的所有事件
        /// </summary>
        private void RegisterAttackControlEvents()
        {
            if (_attackControlFsm == null) return;

            Log.Info("注册Attack Control FSM事件");

            // 创建或获取事件
            _orbitAttackEvent = FsmEvent.GetFsmEvent("ORBIT ATTACK");
            _orbitStartHandLEvent = FsmEvent.GetFsmEvent("ORBIT START Hand L");
            _orbitStartHandREvent = FsmEvent.GetFsmEvent("ORBIT START Hand R");
            _silkBallAttackEvent = FsmEvent.GetFsmEvent("SILK BALL ATTACK");

            // 移动丝球相关事件
            _silkBallStaticEvent = FsmEvent.GetFsmEvent("SILK BALL STATIC");
            _silkBallDashEvent = FsmEvent.GetFsmEvent("SILK BALL DASH");
            _silkBallDashStartEvent = FsmEvent.GetFsmEvent("SILK BALL DASH START");
            _silkBallDashEndEvent = FsmEvent.GetFsmEvent("SILK BALL DASH END");

            // 眩晕中断相关事件
            _silkBallInterruptEvent = FsmEvent.GetFsmEvent("SILK BALL INTERRUPT");
            _silkBallRecoverEvent = FsmEvent.GetFsmEvent("SILK BALL RECOVER");
            _nullEvent = FsmEvent.GetFsmEvent("NULL");
            if (_orbitAttackEvent == null)
            {
                Log.Error("未找到ORBIT ATTACK事件");
                return;
            }
            if (_orbitStartHandLEvent == null)
            {
                Log.Error("未找到ORBIT START Hand L事件");
                return;
            }
            if (_orbitStartHandREvent == null)
            {
                Log.Error("未找到ORBIT START Hand R事件");
                return;
            }
            if (_silkBallAttackEvent == null)
            {
                Log.Error("未找到SILK BALL ATTACK事件");
                return;
            }

            // 将事件添加到FSM的事件列表中
            var existingEvents = _attackControlFsm.FsmEvents.ToList();

            if (!existingEvents.Contains(_orbitAttackEvent))
            {
                existingEvents.Add(_orbitAttackEvent);
            }
            if (!existingEvents.Contains(_orbitStartHandLEvent))
            {
                existingEvents.Add(_orbitStartHandLEvent);
            }
            if (!existingEvents.Contains(_orbitStartHandREvent))
            {
                existingEvents.Add(_orbitStartHandREvent);
            }
            if (!existingEvents.Contains(_silkBallAttackEvent))
            {
                existingEvents.Add(_silkBallAttackEvent);
            }

            // 移动丝球事件
            if (_silkBallStaticEvent != null && !existingEvents.Contains(_silkBallStaticEvent))
            {
                existingEvents.Add(_silkBallStaticEvent);
            }
            if (_silkBallDashEvent != null && !existingEvents.Contains(_silkBallDashEvent))
            {
                existingEvents.Add(_silkBallDashEvent);
            }
            if (_silkBallDashStartEvent != null && !existingEvents.Contains(_silkBallDashStartEvent))
            {
                existingEvents.Add(_silkBallDashStartEvent);
            }
            if (_silkBallDashEndEvent != null && !existingEvents.Contains(_silkBallDashEndEvent))
            {
                existingEvents.Add(_silkBallDashEndEvent);
            }

            // 眩晕中断事件
            if (_silkBallInterruptEvent != null && !existingEvents.Contains(_silkBallInterruptEvent))
            {
                existingEvents.Add(_silkBallInterruptEvent);
            }
            if (_silkBallRecoverEvent != null && !existingEvents.Contains(_silkBallRecoverEvent))
            {
                existingEvents.Add(_silkBallRecoverEvent);
            }

            // 爬升阶段事件
            var climbPhaseAttackEvent = FsmEvent.GetFsmEvent("CLIMB PHASE ATTACK");
            if (climbPhaseAttackEvent != null && !existingEvents.Contains(climbPhaseAttackEvent))
            {
                existingEvents.Add(climbPhaseAttackEvent);
            }
            var climbPhaseEndEvent = FsmEvent.GetFsmEvent("CLIMB PHASE END");
            if (climbPhaseEndEvent != null && !existingEvents.Contains(climbPhaseEndEvent))
            {
                existingEvents.Add(climbPhaseEndEvent);
            }

            // 使用反射设置FsmEvents
            var fsmType = _attackControlFsm.Fsm.GetType();
            var eventsField = fsmType.GetField("events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (eventsField != null)
            {
                eventsField.SetValue(_attackControlFsm.Fsm, existingEvents.ToArray());
                Log.Info("Attack Control FSM事件注册成功");
            }
            else
            {
                Log.Error("Attack Control FSM未找到events字段");
            }
        }

        /// <summary>
        /// 重新链接所有事件引用
        /// </summary>
        private void RelinkAllEventReferences()
        {
            if (_attackControlFsm == null) return;

            Log.Info("重新链接Attack Control FSM事件引用");

            // 重新初始化FSM数据，确保所有事件引用正确
            _attackControlFsm.Fsm.InitData();
            _attackControlFsm.Fsm.InitEvents();

            Log.Info("Attack Control FSM事件引用重新链接完成");
        }
        #endregion
        #region 针环绕攻击方法
        /// <summary>
        /// 初始化手部Behavior组件
        /// </summary>
        private void InitializeHandBehaviors()
        {
            // 查找Hand L和Hand R对象
            handL = GameObject.Find("Hand L");
            handR = GameObject.Find("Hand R");

            Log.Info($"查找手部对象 - Hand L: {(handL != null ? handL.name : "未找到")}, Hand R: {(handR != null ? handR.name : "未找到")}");

            if (handL != null)
            {
                handLBehavior = handL.GetComponent<HandControlBehavior>();
                if (handLBehavior == null)
                {
                    handLBehavior = handL.AddComponent<HandControlBehavior>();
                    Log.Info("Hand L Behavior 组件已添加");
                }
                else
                {
                    Log.Info("Hand L Behavior 组件已存在");
                }
                Log.Info("Hand L Behavior 初始化完成");
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
                    handRBehavior = handR.AddComponent<HandControlBehavior>();
                    Log.Info("Hand R Behavior 组件已添加");
                }
                else
                {
                    Log.Info("Hand R Behavior 组件已存在");
                }
                Log.Info("Hand R Behavior 初始化完成");
            }
            else
            {
                Log.Warn("未找到Hand R对象");
            }
        }
        /// <summary>
        /// 创建环绕攻击状态
        /// </summary>
        private void CreateOrbitAttackState()
        {
            // 创建新状态
            _orbitAttackState = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = newOrbitAttackState,
                Description = "环绕攻击状态"
            };

            // 添加状态到FSM
            var existingStates = _attackControlFsm!.FsmStates.ToList();
            existingStates.Add(_orbitAttackState);
            _attackControlFsm.Fsm.States = existingStates.ToArray();

            // 添加动作到新状态
            AddOrbitAttackActions();
            // 创建子状态和添加转换
            CreateOrbitAttackSubStates();


            Log.Info($"创建新状态: {newOrbitAttackState}");
        }

        /// <summary>
        /// 添加环绕攻击动作 - 新版本：拆分状态，使用FINISHED事件
        /// </summary>
        private void AddOrbitAttackActions()
        {
            Log.Info("添加环绕攻击动作（拆分状态版本）");

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
                sendToHandLAction,
                sendToHandRAction,
                finishAction
            };

            Log.Info($"添加环绕攻击动作完成 - Hand L: {(handL != null ? handL.name : "null")}, Hand R: {(handR != null ? handR.name : "null")}");
        }

        /// <summary>
        /// 创建环绕攻击子状态
        /// </summary>
        private void CreateOrbitAttackSubStates()
        {
            if (_attackControlFsm == null) return;

            var orbitFirstShootState = new FsmState(_attackControlFsm.Fsm)
            {
                Name = "Orbit First Shoot",
                Description = "发送第一个SHOOT事件"
            };


            var orbitSecondShootState = new FsmState(_attackControlFsm.Fsm)
            {
                Name = "Orbit Second Shoot",
                Description = "发送第二个SHOOT事件"
            };

            // 添加状态到FSM
            var existingStates = _attackControlFsm.FsmStates.ToList();
            existingStates.Add(orbitFirstShootState);
            existingStates.Add(orbitSecondShootState);
            _attackControlFsm.Fsm.States = existingStates.ToArray();

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

            Log.Info("环绕攻击子状态创建完成");
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
            // Orbit First Shoot -> Orbit Wait Second
            var firstShootToWaitSecondTransition = new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Orbit Second Shoot",
                toFsmState = orbitSecondShootState
            };
            // Orbit Second Shoot -> Wait For Hands Ready
            var secondShootToFinishTransition = new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Wait For Hands Ready",
                toFsmState = waitForHandsReadyState
            };

            // 设置各状态的转换
            orbitFirstShootState.Transitions = new FsmTransition[] { firstShootToWaitSecondTransition };
            orbitSecondShootState.Transitions = new FsmTransition[] { secondShootToFinishTransition };

            Log.Info("环绕攻击子状态转换设置完成");
        }

        /// <summary>
        /// 添加环绕攻击状态的转换
        /// </summary>
        private void AddOrbitAttackTransitions(FsmState orbitWaitState)
        {
            var transitions = _handPtnChoiceState!.Transitions.ToList();
            transitions.Add(new FsmTransition
            {
                FsmEvent = _orbitAttackEvent,
                toState = newOrbitAttackState,
                toFsmState = _orbitAttackState
            });
            _handPtnChoiceState.Transitions = transitions.ToArray();

            // 添加FINISHED转换，回到Wait For Hands Ready状态
            var finishedTransition = new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Orbit Wait",
                toFsmState = orbitWaitState
            };

            _orbitAttackState!.Transitions = new FsmTransition[] { finishedTransition };

            Log.Info("添加环绕攻击状态转换");
        }
        /// <summary>
        /// 修改HandPtnChoiceState状态的SendRandomEventV4动作，添加环绕攻击事件
        /// </summary>
        private void ModifySendRandomEventAction()
        {
            if (_handPtnChoiceState == null) return;
            var AllSendRandomEventActions = _handPtnChoiceState.Actions.OfType<SendRandomEventV4>().ToList();

            Log.Info("开始修改SendRandomEventV4动作");
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

        #region 丝球攻击

        /// <summary>
        /// 创建丝球环绕攻击的所有状态
        /// </summary>
        private void CreateSilkBallAttackStates()
        {
            Log.Info("=== 开始创建丝球环绕攻击状态链 ===");

            // 创建所有状态（静态版本 + 移动版本）
            _silkBallPrepareState = CreateSilkBallPrepareState();
            _silkBallCastState = CreateSilkBallCastState();
            _silkBallLiftState = CreateSilkBallLiftState();
            _silkBallAnticState = CreateSilkBallAnticState();
            _silkBallReleaseState = CreateSilkBallReleaseState();
            _silkBallEndState = CreateSilkBallEndState();
            _silkBallRecoverState = CreateSilkBallRecoverState();

            // 创建移动版本的状态
            _silkBallDashPrepareState = CreateSilkBallDashPrepareState();
            _silkBallDashEndState = CreateSilkBallDashEndState();

            // 添加到FSM
            var states = _attackControlFsm!.FsmStates.ToList();
            states.Add(_silkBallPrepareState);
            states.Add(_silkBallCastState);
            states.Add(_silkBallLiftState);
            states.Add(_silkBallAnticState);
            states.Add(_silkBallReleaseState);
            states.Add(_silkBallEndState);
            states.Add(_silkBallRecoverState);
            states.Add(_silkBallDashPrepareState);
            states.Add(_silkBallDashEndState);
            _attackControlFsm.Fsm.States = states.ToArray();

            // 查找Idle状态和Move Restart状态用于链接
            var idleState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
            var moveRestartState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Move Restart");

            // 设置状态转换
            // Prepare -> 50% STATIC / 50% DASH + 中断转换
            _silkBallPrepareState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = _silkBallStaticEvent,
                    toState = "Silk Ball Cast",
                    toFsmState = _silkBallCastState
                },
                new FsmTransition
                {
                    FsmEvent = _silkBallDashEvent,
                    toState = "Silk Ball Dash Prepare",
                    toFsmState = _silkBallDashPrepareState
                },
                new FsmTransition
                {
                    FsmEvent = _silkBallInterruptEvent,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                }
            };

            // Dash Prepare -> Dash End (等待BossControl完成) + 中断转换 + 超时保护
            _silkBallDashPrepareState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = _silkBallDashEndEvent,
                    toState = "Silk Ball Dash End",
                    toFsmState = _silkBallDashEndState
                },
                new FsmTransition
                {
                    FsmEvent = _silkBallInterruptEvent,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                },
                // 超时保护：如果30秒内没有收到DASH END事件，强制转移（防止卡死）
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                }
            };

            // Dash End -> Recover
            _silkBallDashEndState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Silk Ball Recover",
                    toFsmState = _silkBallRecoverState
                }
            };

            // Cast -> Lift (通过FINISHED事件) + 中断转换
            _silkBallCastState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Silk Ball Lift",
                    toFsmState = _silkBallLiftState
                },
                new FsmTransition
                {
                    FsmEvent = _silkBallInterruptEvent,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                }
            };

            // Lift -> Antic (上升到高点) + 中断转换 (这个最容易卡死)
            _silkBallLiftState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Silk Ball Antic",
                    toFsmState = _silkBallAnticState
                },
                new FsmTransition
                {
                    FsmEvent = _silkBallInterruptEvent,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                }
            };

            // Antic -> Release (召唤完成后直接Release) + 中断转换
            _silkBallAnticState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Silk Ball Release",
                    toFsmState = _silkBallReleaseState
                },
                new FsmTransition
                {
                    FsmEvent = _silkBallInterruptEvent,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                }
            };

            // Release -> End (通过FINISHED事件) + 中断转换
            _silkBallReleaseState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Silk Ball End",
                    toFsmState = _silkBallEndState
                },
                new FsmTransition
                {
                    FsmEvent = _silkBallInterruptEvent,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                }
            };

            // End -> Recover (通过FINISHED事件)
            _silkBallEndState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Silk Ball Recover",
                    toFsmState = _silkBallRecoverState
                }
            };

            // Recover -> Move Restart (通过FINISHED事件，恢复移动和眩晕控制)
            _silkBallRecoverState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Move Restart",
                    toFsmState = moveRestartState
                }
            };

            // 在Attack Choice状态添加转换到Silk Ball Prepare
            var attackChoiceState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Attack Choice");
            if (attackChoiceState != null)
            {
                var transitions = attackChoiceState.Transitions.ToList();
                transitions.Add(new FsmTransition
                {
                    FsmEvent = _silkBallAttackEvent,
                    toState = "Silk Ball Prepare",
                    toFsmState = _silkBallPrepareState
                });
                attackChoiceState.Transitions = transitions.ToArray();
                Log.Info("添加Attack Choice到Silk Ball Prepare的转换");
            }

            Log.Info("=== 丝球环绕攻击状态链创建完成 ===");
        }
        /// <summary>
        /// 创建Silk Ball Prepare状态（50%静态 / 50%移动）
        /// </summary>
        private FsmState CreateSilkBallPrepareState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Prepare",
                Description = "丝球攻击准备（50%分支）"
            };

            var actions = new List<FsmStateAction>();
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" },

                },
                sendEvent = new FsmString("FORCE IDLE") { Value = "FORCE IDLE" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });


            // 添加50%分支逻辑
            actions.Add(new SendRandomEventV4
            {
                events = new FsmEvent[] { _silkBallStaticEvent!, _silkBallDashEvent! },
                weights = new FsmFloat[] { 1f, 1f },
                eventMax = new FsmInt[] { 2, 2 },
                missedMax = new FsmInt[] { 2, 2 },
                activeBool = new FsmBool { UseVariable = true, Value = true }
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Prepare状态（50%静态/50%移动分支）");
            return state;
        }

        /// <summary>
        /// 修改Attack Choice状态，添加丝球攻击
        /// </summary>
        private void ModifyAttackChoiceForSilkBall()
        {
            var attackChoiceState = _attackControlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Attack Choice");
            if (attackChoiceState == null)
            {
                Log.Error("未找到Attack Choice状态");
                return;
            }

            Log.Info("开始修改Attack Choice状态的两个SendRandomEventV4动作");

            // 按顺序查找所有SendRandomEventV4动作（保持它们在Actions数组中的顺序）
            var sendRandomActions = new List<SendRandomEventV4>();
            for (int i = 0; i < attackChoiceState.Actions.Length; i++)
            {
                if (attackChoiceState.Actions[i] is SendRandomEventV4 sendRandomAction)
                {
                    sendRandomActions.Add(sendRandomAction);
                }
            }

            Log.Info($"找到 {sendRandomActions.Count} 个SendRandomEventV4动作");

            if (sendRandomActions.Count < 2)
            {
                Log.Warn($"找到的SendRandomEventV4动作数量不足（期望2个，实际{sendRandomActions.Count}个）");
                return;
            }

            // 第一个SendRandomEventV4：包含WEB ATTACK
            // 原本：HAND ATTACK 0.6f, DASH ATTACK 0.25f, WEB ATTACK 0.15f
            // 修改为：HAND ATTACK 0.5f, DASH ATTACK 0.15f, WEB ATTACK 0.15f, SILK BALL ATTACK 0.2f
            // eventMax: 3, 1, 1, 1
            // missedMax: 3, 5, 5, 5
            {
                var action = sendRandomActions[0];
                Log.Info("修改第一个SendRandomEventV4（按顺序判断）");

                var newEvents = new FsmEvent[action.events.Length + 1];
                var newWeights = new FsmFloat[action.weights.Length + 1];
                var newEventMax = new FsmInt[action.eventMax.Length + 1];
                var newMissedMax = new FsmInt[action.missedMax.Length + 1];

                for (int i = 0; i < action.events.Length; i++)
                {
                    newEvents[i] = action.events[i];
                    newEventMax[i] = action.eventMax[i];
                    newMissedMax[i] = action.missedMax[i];

                    // 调整权重
                    if (action.events[i] != null && action.events[i].Name == "HAND ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.5f);
                        Log.Info($"第一个动作 - HAND ATTACK权重: {action.weights[i].Value} -> 0.5f");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "DASH ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.15f);
                        Log.Info($"第一个动作 - DASH ATTACK权重: {action.weights[i].Value} -> 0.15f");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "WEB ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.15f);
                        Log.Info($"第一个动作 - WEB ATTACK权重: {action.weights[i].Value} -> 0.15f");
                    }
                    else
                    {
                        newWeights[i] = action.weights[i];
                    }
                }

                // 添加SILK BALL ATTACK
                int newIndex = action.events.Length;
                newEvents[newIndex] = _silkBallAttackEvent!;
                newWeights[newIndex] = new FsmFloat(0.2f);
                newEventMax[newIndex] = new FsmInt(1);
                newMissedMax[newIndex] = new FsmInt(4);

                action.events = newEvents;
                action.weights = newWeights;
                action.eventMax = newEventMax;
                action.missedMax = newMissedMax;

                Log.Info("第一个SendRandomEventV4修改完成：HAND 0.5, DASH 0.15, WEB 0.15, SILK BALL 0.2");
            }

            // 第二个SendRandomEventV4：不包含WEB ATTACK
            // 原本：HAND ATTACK和DASH ATTACK
            // 修改为：HAND ATTACK 0.6f, DASH ATTACK 0.2f, SILK BALL ATTACK 0.2f
            {
                var action = sendRandomActions[1];
                Log.Info("修改第二个SendRandomEventV4（按顺序判断）");

                var newEvents = new FsmEvent[action.events.Length + 1];
                var newWeights = new FsmFloat[action.weights.Length + 1];
                var newEventMax = new FsmInt[action.eventMax.Length + 1];
                var newMissedMax = new FsmInt[action.missedMax.Length + 1];

                for (int i = 0; i < action.events.Length; i++)
                {
                    newEvents[i] = action.events[i];
                    newEventMax[i] = action.eventMax[i];
                    newMissedMax[i] = action.missedMax[i];

                    // 调整权重
                    if (action.events[i] != null && action.events[i].Name == "HAND ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.6f);
                        Log.Info($"第二个动作 - HAND ATTACK权重: {action.weights[i].Value} -> 0.6f");
                    }
                    else if (action.events[i] != null && action.events[i].Name == "DASH ATTACK")
                    {
                        newWeights[i] = new FsmFloat(0.2f);
                        Log.Info($"第二个动作 - DASH ATTACK权重: {action.weights[i].Value} -> 0.2f");
                    }
                    else
                    {
                        newWeights[i] = action.weights[i];
                    }
                }

                // 添加SILK BALL ATTACK
                int newIndex = action.events.Length;
                newEvents[newIndex] = _silkBallAttackEvent!;
                newWeights[newIndex] = new FsmFloat(0.2f);
                newEventMax[newIndex] = new FsmInt(1);
                newMissedMax[newIndex] = new FsmInt(4);

                action.events = newEvents;
                action.weights = newWeights;
                action.eventMax = newEventMax;
                action.missedMax = newMissedMax;

                Log.Info("第二个SendRandomEventV4修改完成：HAND 0.6, DASH 0.2, SILK BALL 0.2");
            }

            Log.Info("Attack Choice状态的SendRandomEventV4修改完成");
        }

        #region 移动丝球攻击
        /// <summary>
        /// 初始化移动丝球相关变量
        /// </summary>
        private void InitializeSilkBallDashVariables()
        {
            if (_attackControlFsm == null) return;

            // 在AttackControl FSM中创建变量
            _isGeneratingSilkBall = _attackControlFsm.FsmVariables.FindFsmBool("Is Generating Silk Ball");
            if (_isGeneratingSilkBall == null)
            {
                _isGeneratingSilkBall = new FsmBool("Is Generating Silk Ball") { Value = false };
                var bools = _attackControlFsm.FsmVariables.BoolVariables.ToList();
                bools.Add(_isGeneratingSilkBall);
                _attackControlFsm.FsmVariables.BoolVariables = bools.ToArray();
            }

            _totalDistanceTraveled = _attackControlFsm.FsmVariables.FindFsmFloat("Total Distance Traveled");
            if (_totalDistanceTraveled == null)
            {
                _totalDistanceTraveled = new FsmFloat("Total Distance Traveled") { Value = 0f };
                var floats = _attackControlFsm.FsmVariables.FloatVariables.ToList();
                floats.Add(_totalDistanceTraveled);
                _attackControlFsm.FsmVariables.FloatVariables = floats.ToArray();
            }

            _lastBallPosition = _attackControlFsm.FsmVariables.FindFsmVector2("Last Ball Position");
            if (_lastBallPosition == null)
            {
                _lastBallPosition = new FsmVector2("Last Ball Position") { Value = Vector2.zero };
                var vec2s = _attackControlFsm.FsmVariables.Vector2Variables.ToList();
                vec2s.Add(_lastBallPosition);
                _attackControlFsm.FsmVariables.Vector2Variables = vec2s.ToArray();
            }

            Log.Info("移动丝球变量初始化完成");
        }
        /// 创建Silk Ball Dash Prepare状态（计算路线并触发移动）
        /// </summary>
        private FsmState CreateSilkBallDashPrepareState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Dash Prepare",
                Description = "移动丝球准备：计算路线并触发Boss移动"
            };

            var actions = new List<FsmStateAction>();

            // 1. 计算并设置路线
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("CalculateAndSetDashRoute") { Value = "CalculateAndSetDashRoute" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 2. 开始丝球生成
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartGeneratingSilkBall") { Value = "StartGeneratingSilkBall" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 3. 发送事件给BossControl，触发移动
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("SILK BALL DASH START") { Value = "SILK BALL DASH START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 4. 等待BossControl发回的SILK BALL DASH END事件（在Transitions中处理）
            // 添加超时保护：30秒后如果还没收到DASH END事件，强制转移（防止卡死）
            actions.Add(new Wait
            {
                time = new FsmFloat(30f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Dash Prepare状态（含30秒超时保护）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Dash End状态（停止生成丝球）
        /// </summary>
        private FsmState CreateSilkBallDashEndState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Dash End",
                Description = "移动丝球结束：停止生成"
            };

            var actions = new List<FsmStateAction>();

            // 1. 停止丝球生成
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopGeneratingSilkBall") { Value = "StopGeneratingSilkBall" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 2. 立即跳转到Recover
            actions.Add(new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Dash End状态");
            return state;
        }
        /// <summary>
        /// 检测并生成丝球（每移动5unity距离）
        /// </summary>
        private void CheckAndSpawnSilkBall()
        {
            Vector2 currentPos = transform.position;

            // 首次调用时初始化位置
            if (_lastBallPosition!.Value == Vector2.zero)
            {
                _lastBallPosition.Value = currentPos;
                return;
            }

            float distance = Vector2.Distance(currentPos, _lastBallPosition.Value);
            _totalDistanceTraveled!.Value += distance;
            _lastBallPosition.Value = currentPos;

            // 每移动5unity距离生成一个丝球
            if (_totalDistanceTraveled.Value >= 5f)
            {
                SpawnSilkBallAtPosition(currentPos);
                _totalDistanceTraveled.Value = 0f;
            }
        }

        /// <summary>
        /// 在指定位置生成丝球，等待0.1s后释放
        /// </summary>
        private void SpawnSilkBallAtPosition(Vector2 position)
        {
            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager未初始化，无法生成丝球");
                return;
            }

            Vector3 spawnPos = new Vector3(position.x, position.y, 0f);
            var behavior = _silkBallManager.SpawnSilkBall(spawnPos, 35f, 25f, 8f, 1f, true);

            if (behavior != null)
            {
                // 延迟0.1s后释放（给丝球初始化时间）
                StartCoroutine(DelayedReleaseSilkBallForDash(behavior.gameObject));

                Log.Info($"移动丝球生成成功: {spawnPos}");
            }
        }

        /// <summary>
        /// 延迟释放移动丝球（0.1s）
        /// </summary>
        private IEnumerator DelayedReleaseSilkBallForDash(GameObject silkBall)
        {
            if (silkBall != null)
            {
                yield return null;
                silkBall.LocateMyFSM("Control").SendEvent("PREPARE");
                yield return new WaitForSeconds(0.1f);
                var fsm = silkBall.GetComponent<PlayMakerFSM>();
                if (fsm != null)
                {
                    fsm.SendEvent("SILK BALL RELEASE");
                }
            }
        }

        /// <summary>
        /// 计算并设置移动路线到BossControl FSM
        /// </summary>
        public void CalculateAndSetDashRoute()
        {
            if (_bossControlFsm == null)
            {
                Log.Error("BossControl FSM未初始化，无法设置路线");
                return;
            }

            Vector2 bossPos = transform.position;

            // 从BossControl FSM获取Hero Is Far变量
            var heroIsFar = _bossControlFsm.FsmVariables.FindFsmBool("Hero Is Far");
            bool isFar = heroIsFar != null && heroIsFar.Value;

            // 判断Boss区域
            BossZone zone = GetBossZone(bossPos.x);

            Vector3 point0, point1, point2;

            if (zone == BossZone.Middle)
            {
                // 中区：50%随机选左上或右上
                bool goLeft = UnityEngine.Random.value > 0.5f;
                point0 = goLeft ? POS_LEFT_UP : POS_RIGHT_UP;
                point1 = goLeft ? POS_RIGHT_UP : POS_LEFT_UP;
                Log.Info($"中区路线: {(goLeft ? "左上→右上" : "右上→左上")}→中下");
            }
            else if (isFar)
            {
                // 两侧 + 远距离：中上 → 对应角
                point0 = POS_MIDDLE_UP;
                point1 = (zone == BossZone.Left) ? POS_LEFT_UP : POS_RIGHT_UP;
                Log.Info($"{zone}区+远距离: 中上→{(zone == BossZone.Left ? "左上" : "右上")}→中下");
            }
            else
            {
                // 两侧 + 近距离：对角线
                if (zone == BossZone.Left)
                {
                    point0 = POS_RIGHT_UP;
                    point1 = POS_LEFT_UP;
                    Log.Info("左区+近距离: 右上→左上→中下");
                }
                else
                {
                    point0 = POS_LEFT_UP;
                    point1 = POS_RIGHT_UP;
                    Log.Info("右区+近距离: 左上→右上→中下");
                }
            }

            // 最终点都是中下
            point2 = POS_MIDDLE_DOWN;

            // 设置到BossControl的Float变量（X和Y分开）
            SetRoutePoint(0, point0);
            SetRoutePoint(1, point1);
            SetRoutePoint(2, point2);

            // 同时更新隐形目标点GameObject的位置
            var bossBehavior = gameObject.GetComponent<BossBehavior>();
            if (bossBehavior != null)
            {
                bossBehavior.UpdateTargetPointPositions(point0, point1, point2);
            }
            else
            {
                Log.Warn("未找到BossBehavior组件，无法更新隐形目标点位置");
            }

            Log.Info($"路线已设置: {point0} → {point1} → {point2}");
        }

        /// <summary>
        /// 判断Boss所在区域
        /// </summary>
        private BossZone GetBossZone(float x)
        {
            if (x < ZONE_LEFT_MAX) return BossZone.Left;
            if (x > ZONE_RIGHT_MIN) return BossZone.Right;
            return BossZone.Middle;
        }

        /// <summary>
        /// 设置路线点位（通过BossBehavior的public变量直接设置）
        /// </summary>
        private void SetRoutePoint(int index, Vector3 value)
        {
            // 获取BossBehavior组件
            var bossBehavior = gameObject.GetComponent<BossBehavior>();
            if (bossBehavior == null)
            {
                Log.Error("未找到BossBehavior组件，无法设置路线点");
                return;
            }

            // 根据索引设置对应的public变量
            switch (index)
            {
                case 0:
                    if (bossBehavior.RoutePoint0X != null && bossBehavior.RoutePoint0Y != null)
                    {
                        bossBehavior.RoutePoint0X.Value = value.x;
                        bossBehavior.RoutePoint0Y.Value = value.y;
                        Log.Info($"设置路线点 0: X={value.x}, Y={value.y}");
                    }
                    else
                    {
                        Log.Error("RoutePoint0X 或 RoutePoint0Y 为 null");
                    }
                    break;
                case 1:
                    if (bossBehavior.RoutePoint1X != null && bossBehavior.RoutePoint1Y != null)
                    {
                        bossBehavior.RoutePoint1X.Value = value.x;
                        bossBehavior.RoutePoint1Y.Value = value.y;
                        Log.Info($"设置路线点 1: X={value.x}, Y={value.y}");
                    }
                    else
                    {
                        Log.Error("RoutePoint1X 或 RoutePoint1Y 为 null");
                    }
                    break;
                case 2:
                    if (bossBehavior.RoutePoint2X != null && bossBehavior.RoutePoint2Y != null)
                    {
                        bossBehavior.RoutePoint2X.Value = value.x;
                        bossBehavior.RoutePoint2Y.Value = value.y;
                        Log.Info($"设置路线点 2: X={value.x}, Y={value.y}");
                    }
                    else
                    {
                        Log.Error("RoutePoint2X 或 RoutePoint2Y 为 null");
                    }
                    break;
                default:
                    Log.Error($"无效的路线点索引: {index}");
                    break;
            }
        }

        /// <summary>
        /// 开始移动丝球生成（供FSM调用）
        /// </summary>
        public void StartGeneratingSilkBall()
        {
            if (_isGeneratingSilkBall != null)
            {
                _isGeneratingSilkBall.Value = true;
                _totalDistanceTraveled!.Value = 0f;
                _lastBallPosition!.Value = transform.position;
                Log.Info("开始生成移动丝球");
            }
        }

        /// <summary>
        /// 停止移动丝球生成（供FSM调用）
        /// </summary>
        public void StopGeneratingSilkBall()
        {
            if (_isGeneratingSilkBall != null)
            {
                _isGeneratingSilkBall.Value = false;
                // 重置移动相关变量
                if (_totalDistanceTraveled != null)
                {
                    _totalDistanceTraveled.Value = 0f;
                }
                if (_lastBallPosition != null)
                {
                    _lastBallPosition.Value = Vector2.zero;
                }
                Log.Info("停止生成移动丝球并重置变量");
            }
        }
        #endregion

        #region 丝球环绕攻击方法

        /// <summary>
        /// 召唤8个丝球的协程（在高点一个个召唤）
        /// </summary>
        public IEnumerator SummonSilkBallsAtHighPointCoroutine()
        {
            Log.Info("=== 开始在高点召唤8个丝球 ===");
            _activeSilkBalls.Clear();
            Vector3 bossPosition = transform.position;
            float radius = 6f;

            // 逐个召唤丝球
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f; // 0°, 45°, 90°, 135°, 180°, 225°, 270°, 315°
                float radians = angle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(
                    Mathf.Cos(radians) * radius,
                    Mathf.Sin(radians) * radius,
                    0f
                );
                Vector3 spawnPosition = bossPosition + offset;

                // 召唤丝球
                var behavior = _silkBallManager?.SpawnSilkBall(spawnPosition, 30f, 25f, 8f, 1f, true);
                if (behavior != null)
                {
                    yield return null;
                    _activeSilkBalls.Add(behavior.gameObject);
                    behavior.gameObject.LocateMyFSM("Control").SendEvent("PREPARE");
                    Log.Info($"召唤第 {i + 1} 个丝球");
                }

                // 等待0.1秒后再召唤下一个（给丝球初始化和音效播放时间）
                yield return new WaitForSeconds(0.1f);
            }

            Log.Info($"=== 8个丝球召唤完成，共 {_activeSilkBalls.Count} 个，等待准备完成 ===");

            // 等待所有丝球准备完成
            float maxWaitTime = 1f;
            float elapsedTime = 0f;
            bool allPrepared = false;

            while (elapsedTime < maxWaitTime && !allPrepared)
            {
                allPrepared = true;
                foreach (var ball in _activeSilkBalls)
                {
                    if (ball != null)
                    {
                        var ballBehavior = ball.GetComponent<SilkBallBehavior>();
                        if (ballBehavior != null && !ballBehavior.isPrepared)
                        {
                            allPrepared = false;
                            break;
                        }
                    }
                }

                if (!allPrepared)
                {
                    yield return new WaitForSeconds(0.05f);
                    elapsedTime += 0.05f;
                }
            }

            if (allPrepared)
            {
                Log.Info("=== 所有丝球已准备完成 ===");
            }
            else
            {
                Log.Warn($"=== 等待超时，部分丝球可能未准备完成（已等待 {elapsedTime:F2}秒） ===");
            }
        }

        /// <summary>
        /// 供FSM调用的方法：开始丝球召唤（在高点）
        /// </summary>
        public void StartSilkBallSummonAtHighPoint()
        {
            Log.Info("供FSM调用：开始在高点召唤丝球");
            if (_silkBallSummonCoroutine != null)
            {
                StopCoroutine(_silkBallSummonCoroutine);
            }
            _silkBallSummonCoroutine = StartCoroutine(SummonSilkBallsAtHighPointCoroutine());
        }

        /// <summary>
        /// 释放所有丝球
        /// </summary>
        public void ReleaseSilkBalls()
        {
            StartCoroutine(ReleaseSilkBallsCoroutine());
        }

        private IEnumerator ReleaseSilkBallsCoroutine()
        {
            // 等待0.82秒（动画时间）
            yield return new WaitForSeconds(0.82f);

            Log.Info($"=== 准备释放丝球 ===");

            // 在释放丝球时触发冲击波效果和音效（使用缓存的动作）
            // 1. 触发冲击波效果
            if (_cachedRoarEmitter != null)
            {
                _cachedRoarEmitter.OnEnter();
            }

            // 2. 播放尖叫音效
            if (_cachedPlayRoarAudio != null)
            {
                _cachedPlayRoarAudio.OnEnter();
            }

            // 检查丝球列表
            if (_activeSilkBalls == null || _activeSilkBalls.Count == 0)
            {
                Log.Warn($"警告：丝球列表为空或null（数量: {_activeSilkBalls?.Count ?? 0}），无法释放！");
                yield break;
            }

            Log.Info($"当前丝球列表数量: {_activeSilkBalls.Count}");

            // 释放所有丝球
            int successCount = 0;
            int preparedCount = 0;
            foreach (var ball in _activeSilkBalls)
            {
                if (ball != null)
                {
                    var behavior = ball.GetComponent<SilkBallBehavior>();
                    if (behavior != null)
                    {
                        if (behavior.isPrepared)
                        {
                            preparedCount++;
                        }

                        var fsm = ball.GetComponent<PlayMakerFSM>();
                        if (fsm != null)
                        {
                            ball.LocateMyFSM("Control").SendEvent("SILK BALL RELEASE");
                            successCount++;
                        }
                        else
                        {
                            Log.Error($"✗ 丝球 {ball.name} 没有PlayMakerFSM组件！");
                        }
                    }
                    else
                    {
                        Log.Error($"✗ 丝球 {ball.name} 没有SilkBallBehavior组件！");
                    }
                }
                else
                {
                    Log.Error("✗ 列表中有null丝球对象！");
                }
            }

            Log.Info($"=== 释放完成：成功 {successCount}/{_activeSilkBalls.Count} 个，其中已准备 {preparedCount} 个 ===");
            _activeSilkBalls.Clear();

            // 等待0.2秒后退出协程
            yield return new WaitForSeconds(0.2f);
            if (_cachedRoarEmitter != null)
            {
                _cachedRoarEmitter.OnExit();
            }
        }
        /// <summary>
        /// 创建Silk Ball Cast状态（播放Cast动画并向上移动）
        /// </summary>
        private FsmState CreateSilkBallCastState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Cast",
                Description = "丝球攻击Cast动画，同时向上移动"
            };

            var actions = new List<FsmStateAction>();
            //  停止眩晕控制
            var stopStunControl = CloneAction<SendEventByName>("Web Lift");
            if (stopStunControl != null)
            {
                actions.Add(stopStunControl);
            }
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.UseOwner
                },
                fsmName = new FsmString("Control") { Value = "Control" },
                variableName = new FsmString("Attack Prepare") { Value = "Attack Prepare" },
                setValue = new FsmBool(true),
                everyFrame = false
            });
            //  清空速度
            var setVelocity = CloneAction<SetVelocity2d>("Web Cast");
            if (setVelocity != null)
            {
                actions.Add(setVelocity);
            }

            //  播放Cast动画
            actions.Add(new Tk2dPlayAnimationWithEventsV2
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Cast") { Value = "Cast" },
                animationTriggerEvent = FsmEvent.Finished,
                animationCompleteEvent = _nullEvent,
                animationInterruptedEvent = _nullEvent,
            });
            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Cast状态（Cast动画 + 向上移动）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Lift状态（只负责向上移动到高点）
        /// </summary>
        private FsmState CreateSilkBallLiftState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Lift",
                Description = "向上移动到高点"
            };

            var actions = new List<FsmStateAction>();

            // 1. 设置向上速度
            var setVelocityUp = CloneAction<SetVelocity2d>("Web Lift", matchIndex: 0);
            if (setVelocityUp != null)
            {
                actions.Add(setVelocityUp);
            }

            // 2. 减速
            var decelerate = CloneAction<DecelerateV2>("Web Lift");
            if (decelerate != null)
            {
                actions.Add(decelerate);
            }

            actions.Add(new Tk2dWatchAnimationEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animationCompleteEvent = _nullEvent,
                animationTriggerEvent = FsmEvent.Finished,
            });

            // 4. 检查是否到达高度
            var checkHeight = CloneAction<CheckYPositionV2>("Web Lift");
            if (checkHeight != null)
            {
                actions.Add(checkHeight);
            }

            // 5. 到达高度后停止
            var stopAtHeight = CloneAction<SetVelocity2dBool>("Web Lift");
            if (stopAtHeight != null)
            {
                actions.Add(stopAtHeight);
            }

            // 添加超时保护：2秒后自动触发FINISHED事件（防止动画被打断导致卡死）
            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Lift状态（只上升 + 2秒超时保护）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Antic状态（在高点召唤丝球）
        /// </summary>
        private FsmState CreateSilkBallAnticState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Antic",
                Description = "在高点召唤8个丝球"
            };

            var actions = new List<FsmStateAction>();

            // 1. 清空速度，保持悬浮（使用CloneAction确保正确初始化）
            var setVelocity = CloneAction<SetVelocity2d>("Roar Antic", matchIndex: 0);
            if (setVelocity != null)
            {
                actions.Add(setVelocity);
            }

            // 2. 开始召唤丝球
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartSilkBallSummonAtHighPoint") { Value = "StartSilkBallSummonAtHighPoint" },
                parameters = new FsmVar[0],
                everyFrame = false
            });
            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Antic状态（在高点召唤）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Release状态（释放丝球 + 冲击波，无Roar动画）
        /// </summary>
        private FsmState CreateSilkBallReleaseState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Release",
                Description = "释放所有丝球并触发冲击波"
            };

            var actions = new List<FsmStateAction>();

            // 释放所有丝球（冲击波和音效在协程内触发）
            var releaseBallsAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ReleaseSilkBalls") { Value = "ReleaseSilkBalls" },
                parameters = new FsmVar[0],
                everyFrame = false
            };
            actions.Add(releaseBallsAction);

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Release状态（无Roar动画）");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball End状态
        /// </summary>
        private FsmState CreateSilkBallEndState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball End",
                Description = "丝球攻击结束"
            };

            var actions = new List<FsmStateAction>();
            actions.Add(new Tk2dWatchAnimationEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animationCompleteEvent = FsmEvent.Finished,
                animationTriggerEvent = _nullEvent,
            });


            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball End状态");
            return state;
        }

        /// <summary>
        /// 创建Silk Ball Recover状态（简单下降，然后交给Move Restart恢复）
        /// </summary>
        private FsmState CreateSilkBallRecoverState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Recover",
                Description = "丝球攻击结束，简单下降"
            };

            var actions = new List<FsmStateAction>();

            // 1. 发送IDLE事件
            var sendIdle = CloneAction<SendEventByName>("Web Recover", predicate: action =>
            {
                var eventName = action.sendEvent?.Value;
                return !string.IsNullOrEmpty(eventName) && eventName.Equals("IDLE", StringComparison.OrdinalIgnoreCase);
            });
            if (sendIdle != null)
            {
                actions.Add(sendIdle);
            }

            // 2. 播放Idle动画
            var playIdleAnim = CloneAction<Tk2dPlayAnimation>("Web Recover");
            if (playIdleAnim != null)
            {
                actions.Add(playIdleAnim);
            }

            // 3. 设置向下速度
            var setVelocityDown = CloneAction<SetVelocity2d>("Web Recover");
            if (setVelocityDown != null)
            {
                actions.Add(setVelocityDown);
            }

            // 4. 减速下降
            var decelerate = CloneAction<DecelerateV2>("Web Recover");
            if (decelerate != null)
            {
                actions.Add(decelerate);
            }

            // 5. 等待0.5秒后转到Move Restart
            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            Log.Info("创建Silk Ball Recover状态（下降后转Move Restart）");
            return state;
        }
        #endregion

        #endregion

        private FsmStateAction[] CloneStateActions(string sourceStateName, Func<FsmStateAction, bool>? skipPredicate = null)
        {
            if (_attackControlFsm == null)
            {
                Log.Warn("CloneStateActions调用时_attackControlFsm为null");
                return Array.Empty<FsmStateAction>();
            }

            var sourceState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == sourceStateName);
            if (sourceState == null)
            {
                Log.Warn($"未找到用于克隆的状态: {sourceStateName}");
                return Array.Empty<FsmStateAction>();
            }

            var clonedActions = new List<FsmStateAction>(sourceState.Actions.Length);
            foreach (var action in sourceState.Actions)
            {
                if (action == null)
                {
                    continue;
                }

                if (skipPredicate != null && skipPredicate(action))
                {
                    Log.Info($"跳过克隆动作 {action.GetType().Name} 来自 {sourceStateName}");
                    continue;
                }

                clonedActions.Add(CloneAction(action));
            }

            return clonedActions.ToArray();
        }

        private FsmStateAction CloneAction(FsmStateAction action)
        {
            var type = action.GetType();
            if (Activator.CreateInstance(type) is not FsmStateAction clone)
            {
                Log.Warn($"无法克隆动作: {type.FullName}");
                return action;
            }

            CopyActionFields(action, clone, type);
            return clone;
        }

        private void CopyActionFields(object source, object target, Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(flags))
            {
                field.SetValue(target, field.GetValue(source));
            }

            var baseType = type.BaseType;
            if (baseType != null && baseType != typeof(object))
            {
                CopyActionFields(source, target, baseType);
            }
        }

        private T? CloneAction<T>(string sourceStateName, int matchIndex = 0, Func<T, bool>? predicate = null) where T : FsmStateAction
        {
            if (_attackControlFsm == null)
            {
                Log.Warn("CloneAction调用时_attackControlFsm为null");
                return null;
            }

            var sourceState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == sourceStateName);
            if (sourceState == null)
            {
                Log.Warn($"未找到用于克隆的状态: {sourceStateName}");
                return null;
            }

            int currentIndex = 0;
            foreach (var action in sourceState.Actions)
            {
                if (action is not T typedAction)
                {
                    continue;
                }

                if (predicate != null && !predicate(typedAction))
                {
                    continue;
                }

                if (currentIndex == matchIndex)
                {
                    return (T)CloneAction(typedAction);
                }

                currentIndex++;
            }

            Log.Warn($"在状态{sourceStateName}中未找到类型{typeof(T).Name}的第{matchIndex}个匹配动作");
            return null;
        }
        public T? CloneActionFromAttackControlFSM<T>(string sourceStateName, int matchIndex = 0, Func<T, bool>? predicate = null) where T : FsmStateAction
        {
            return CloneAction<T>(sourceStateName, matchIndex, predicate);
        }

        #region 原版攻击调整
        /// <summary>
        /// 原版攻击相关调整：为了某些特殊玩法兼容，自动复制Pattern 1为Pattern 3
        /// </summary>
        private void PatchOriginalAttackPatterns()
        {
            if (_strandPatterns == null)
            {
                Log.Warn("_strandPatterns为null，无法调整原版攻击Pattern！");
                return;
            }
            // 查找Pattern 1
            var pattern1 = _strandPatterns.transform.Find("Pattern 1");
            if (pattern1 == null)
            {
                Log.Warn("未找到 Pattern 1，原版攻击调整跳过");
                return;
            }
            // 检查是否已存在Pattern 3，避免重复
            if (_strandPatterns.transform.Find("Pattern 3") != null)
            {
                Log.Info("Pattern 3 已存在，无需再次复制");
                return;
            }
            // 深克隆Pattern 1，创建Pattern 3
            var pattern3Obj = GameObject.Instantiate(pattern1.gameObject, _strandPatterns.transform);
            pattern3Obj.name = "Pattern 3";
            Log.Info("已自动复制Pattern 1为Pattern 3");
            PatchSingleAndDoubleStatesLastActions();
            PatchSingleAndDoubleStatesLastActionsV2();
        }
        /// <summary>
        /// 调整Single和Double状态的Action队列，把Double的最后两个Action（GetRandomChild/SendEventByName）补到二者末尾
        /// </summary>
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
            // 克隆
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
        /// <summary>
        /// Double加Triple状态，Triple做复制攻击/延时1s后转Web Recover
        /// </summary>
        private void PatchSingleAndDoubleStatesLastActionsV2()
        {
            if (_attackControlFsm == null) return;
            var doubleState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Double");
            var webRecoverState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Web Recover");
            if (doubleState == null || webRecoverState == null)
            {
                Log.Warn("Double或Web Recover状态不存在，无法补丁Triple攻击链");
                return;
            }
            // 克隆Double里的GetRandomChild与SendEventByName
            var atkAction = CloneAction<GetRandomChild>("Double");
            var sendEventAction = CloneAction<SendEventByName>("Double");
            if (atkAction == null || sendEventAction == null)
            {
                Log.Warn("Double克隆攻击动作失败");
                return;
            }
            sendEventAction.delay = new FsmFloat(0.8f);
            // Triple挂载这俩
            var waitAction = new Wait { time = new FsmFloat(1.0f), finishEvent = FsmEvent.Finished };
            var tripleState = new FsmState(_attackControlFsm.Fsm)
            {
                Name = "Triple",
                Description = "补丁三连击：1次GetRandomChild+SendEvent+延时1s"
            };
            tripleState.Actions = new FsmStateAction[] { atkAction, sendEventAction, waitAction };
            // 插入FSM列表
            var allStates = _attackControlFsm.FsmStates.ToList();
            allStates.Add(tripleState);
            _attackControlFsm.Fsm.States = allStates.ToArray();
            // 把Double所有to Web Recover的跳转改成Triple
            foreach (var trans in doubleState.Transitions)
            {
                if (trans.toState == "Web Recover" || trans.toFsmState == webRecoverState)
                {
                    trans.toState = "Triple";
                    trans.toFsmState = tripleState;
                }
            }
            // Triple唯一跳转：FINISHED -> Web Recover
            tripleState.Transitions = new FsmTransition[] {
                new FsmTransition {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Web Recover",
                    toFsmState = webRecoverState
                }
            };
            Log.Info("已自动插入补丁Triple攻击链，并更新Double跳转");
        }
        // 请在PatchOriginalAttackPatterns最后替换调用PatchSingleAndDoubleStatesLastActionsV2，原有V1方法弃用
        #endregion

        #region 爬升阶段攻击系统

        /// <summary>
        /// 创建爬升阶段攻击状态链
        /// </summary>
        private void CreateClimbPhaseAttackStates()
        {
            if (_attackControlFsm == null) return;

            Log.Info("=== 开始创建爬升阶段攻击状态链 ===");

            // 创建攻击状态
            var climbAttackChoice = CreateClimbAttackChoiceState();
            var climbNeedleAttack = CreateClimbNeedleAttackState();
            var climbWebAttack = CreateClimbWebAttackState();
            var climbSilkBallAttack = CreateClimbSilkBallAttackState();
            var climbAttackCooldown = CreateClimbAttackCooldownState();

            // 找到Idle状态用于转换
            var idleState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");

            // 添加状态到FSM
            var states = _attackControlFsm.FsmStates.ToList();
            states.Add(climbAttackChoice);
            states.Add(climbNeedleAttack);
            states.Add(climbWebAttack);
            states.Add(climbSilkBallAttack);
            states.Add(climbAttackCooldown);
            _attackControlFsm.Fsm.States = states.ToArray();

            // 添加动作
            AddClimbAttackChoiceActions(climbAttackChoice);
            AddClimbNeedleAttackActions(climbNeedleAttack);
            AddClimbWebAttackActions(climbWebAttack);
            AddClimbSilkBallAttackActions(climbSilkBallAttack);
            AddClimbAttackCooldownActions(climbAttackCooldown);

            // 添加转换
            AddClimbAttackTransitions(climbAttackChoice, climbNeedleAttack,
                climbWebAttack, climbSilkBallAttack, climbAttackCooldown);

            // 添加全局转换
            AddClimbPhaseAttackGlobalTransitions(climbAttackChoice, idleState);

            // 初始化FSM
            _attackControlFsm.Fsm.InitData();
            _attackControlFsm.Fsm.InitEvents();

            Log.Info("=== 爬升阶段攻击状态链创建完成 ===");
        }

        private FsmState CreateClimbAttackChoiceState()
        {
            return new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Climb Attack Choice",
                Description = "爬升阶段攻击选择"
            };
        }

        private FsmState CreateClimbNeedleAttackState()
        {
            return new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Climb Needle Attack",
                Description = "爬升阶段针攻击"
            };
        }

        private FsmState CreateClimbWebAttackState()
        {
            return new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Climb Web Attack",
                Description = "爬升阶段网攻击"
            };
        }

        private FsmState CreateClimbSilkBallAttackState()
        {
            return new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Climb Silk Ball Attack",
                Description = "爬升阶段丝球攻击"
            };
        }

        private FsmState CreateClimbAttackCooldownState()
        {
            return new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Climb Attack Cooldown",
                Description = "爬升阶段攻击冷却"
            };
        }

        private void AddClimbAttackChoiceActions(FsmState choiceState)
        {
            var actions = new List<FsmStateAction>();

            // 随机选择攻击类型
            var climbNeedleEvent = FsmEvent.GetFsmEvent("CLIMB NEEDLE ATTACK");
            var climbWebEvent = FsmEvent.GetFsmEvent("CLIMB WEB ATTACK");
            var climbSilkBallEvent = FsmEvent.GetFsmEvent("CLIMB SILK BALL ATTACK");

            actions.Add(new SendRandomEventV4
            {
                events = new FsmEvent[] { climbNeedleEvent, climbWebEvent, climbSilkBallEvent },
                weights = new FsmFloat[] { new FsmFloat(1.0f), new FsmFloat(0.8f), new FsmFloat(0.6f) },
                eventMax = new FsmInt[] { new FsmInt(1), new FsmInt(1), new FsmInt(1) },
                missedMax = new FsmInt[] { new FsmInt(2), new FsmInt(2), new FsmInt(3) },
                activeBool = new FsmBool { UseVariable = true, Value = true }
            });

            choiceState.Actions = actions.ToArray();
        }

        private void AddClimbNeedleAttackActions(FsmState needleState)
        {
            var actions = new List<FsmStateAction>();

            // 执行针攻击（占位，后续实现）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbNeedleAttack") { Value = "ExecuteClimbNeedleAttack" },
                parameters = new FsmVar[0]
            });

            // 等待0.8秒
            actions.Add(new Wait
            {
                time = new FsmFloat(0.8f),
                finishEvent = FsmEvent.Finished
            });

            needleState.Actions = actions.ToArray();
        }

        private void AddClimbWebAttackActions(FsmState webState)
        {
            var actions = new List<FsmStateAction>();

            // 执行网攻击（占位，后续实现）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbWebAttack") { Value = "ExecuteClimbWebAttack" },
                parameters = new FsmVar[0]
            });

            // 等待1.0秒
            actions.Add(new Wait
            {
                time = new FsmFloat(1.0f),
                finishEvent = FsmEvent.Finished
            });

            webState.Actions = actions.ToArray();
        }

        private void AddClimbSilkBallAttackActions(FsmState silkBallState)
        {
            var actions = new List<FsmStateAction>();

            // 执行丝球攻击（占位，后续实现）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbSilkBallAttack") { Value = "ExecuteClimbSilkBallAttack" },
                parameters = new FsmVar[0]
            });

            // 等待1.2秒
            actions.Add(new Wait
            {
                time = new FsmFloat(1.2f),
                finishEvent = FsmEvent.Finished
            });

            silkBallState.Actions = actions.ToArray();
        }

        private void AddClimbAttackCooldownActions(FsmState cooldownState)
        {
            var actions = new List<FsmStateAction>();

            // 等待2-3秒冷却
            actions.Add(new Wait
            {
                time = new FsmFloat(UnityEngine.Random.Range(2f, 3f)),
                finishEvent = FsmEvent.Finished
            });

            cooldownState.Actions = actions.ToArray();
        }

        private void AddClimbAttackTransitions(FsmState choiceState, FsmState needleState,
            FsmState webState, FsmState silkBallState, FsmState cooldownState)
        {
            // Choice -> 各种攻击
            choiceState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB NEEDLE ATTACK"),
                    toState = "Climb Needle Attack",
                    toFsmState = needleState
                },
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB WEB ATTACK"),
                    toState = "Climb Web Attack",
                    toFsmState = webState
                },
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB SILK BALL ATTACK"),
                    toState = "Climb Silk Ball Attack",
                    toFsmState = silkBallState
                }
            };

            // 各攻击 -> Cooldown
            needleState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Attack Cooldown",
                    toFsmState = cooldownState
                }
            };

            webState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Attack Cooldown",
                    toFsmState = cooldownState
                }
            };

            silkBallState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Attack Cooldown",
                    toFsmState = cooldownState
                }
            };

            // Cooldown -> Choice (循环)
            cooldownState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Attack Choice",
                    toFsmState = choiceState
                }
            };

            Log.Info("爬升阶段攻击转换设置完成");
        }

        private void AddClimbPhaseAttackGlobalTransitions(FsmState climbAttackChoice, FsmState? idleState)
        {
            var globalTransitions = _attackControlFsm!.Fsm.GlobalTransitions.ToList();

            // 收到 CLIMB PHASE ATTACK → Climb Attack Choice
            globalTransitions.Add(new FsmTransition
            {
                FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE ATTACK"),
                toState = "Climb Attack Choice",
                toFsmState = climbAttackChoice
            });

            // 收到 CLIMB PHASE END → Idle
            if (idleState != null)
            {
                globalTransitions.Add(new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE END"),
                    toState = "Idle",
                    toFsmState = idleState
                });
            }

            _attackControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
            Log.Info("爬升阶段攻击全局转换添加完成");
        }

        /// <summary>
        /// 执行爬升阶段针攻击（随机一只Hand，相当于之前的环绕攻击）
        /// </summary>
        public void ExecuteClimbNeedleAttack()
        {
            Log.Info("执行爬升阶段针攻击（随机Hand）");
            StartCoroutine(ClimbNeedleAttackCoroutine());
        }

        private IEnumerator ClimbNeedleAttackCoroutine()
        {
            // 随机选择一只Hand（0或1）
            int handIndex = UnityEngine.Random.Range(0, 2);
            Log.Info($"选择Hand {handIndex}进行环绕攻击");

            // 发送事件给对应的Hand FSM
            var bossObj = gameObject;
            var handFsm = bossObj.LocateMyFSM("Hand FSM");
            if (handFsm != null)
            {
                // 设置Hand索引变量
                var handIndexVar = handFsm.FsmVariables.FindFsmInt("Hand Index");
                if (handIndexVar != null)
                {
                    handIndexVar.Value = handIndex;
                }

                // 发送环绕攻击事件
                handFsm.SendEvent("ORBIT ATTACK");
                Log.Info($"已发送ORBIT ATTACK事件给Hand {handIndex}");
            }

            yield return new WaitForSeconds(2f);
        }

        /// <summary>
        /// 执行爬升阶段网攻击（双网旋转）
        /// </summary>
        public void ExecuteClimbWebAttack()
        {
            Log.Info("执行爬升阶段网攻击（双网旋转）");
            StartCoroutine(ClimbWebAttackCoroutine());
        }

        private IEnumerator ClimbWebAttackCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController未找到，无法执行网攻击");
                yield break;
            }

            Vector3 playerPos = hero.transform.position;

            // 第一根网：随机30~-30°
            float firstAngle = UnityEngine.Random.Range(-30f, 30f);
            SpawnClimbWebAtAngle(playerPos, firstAngle);
            Log.Info($"生成第一根网，角度: {firstAngle}°");

            yield return new WaitForSeconds(0.5f);

            // 第二根网：在第一根的基础上旋转90°
            float secondAngle = firstAngle + 90f;
            SpawnClimbWebAtAngle(playerPos, secondAngle);
            Log.Info($"生成第二根网，角度: {secondAngle}°");

            yield return new WaitForSeconds(1f);
        }

        private void SpawnClimbWebAtAngle(Vector3 playerPos, float angle)
        {
            // 获取SingleWebManager
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj == null)
            {
                Log.Warn("未找到AnySilkBossManager");
                return;
            }

            var webManager = managerObj.GetComponent<Managers.SingleWebManager>();
            if (webManager == null)
            {
                Log.Warn("未找到SingleWebManager组件");
                return;
            }

            // 使用欧拉角设置旋转
            Vector3 rotation = new Vector3(0f, 0f, angle);
            
            // 生成网并触发攻击
            webManager.SpawnAndAttack(playerPos, rotation, null, 0f, 0.75f);
            Log.Info($"在玩家位置生成网，角度: {angle}°");
        }

        /// <summary>
        /// 执行爬升阶段丝球攻击（四角生成）
        /// </summary>
        public void ExecuteClimbSilkBallAttack()
        {
            Log.Info("执行爬升阶段丝球攻击（四角生成）");
            StartCoroutine(ClimbSilkBallAttackCoroutine());
        }

        private IEnumerator ClimbSilkBallAttackCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController未找到，无法执行丝球攻击");
                yield break;
            }

            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager未初始化");
                yield break;
            }

            Vector3 playerPos = hero.transform.position;
            float offset = 15f;

            // 四个方向：左上、左下、右上、右下
            Vector3[] positions = new Vector3[]
            {
                playerPos + new Vector3(-offset, offset, 0f),   // 左上
                playerPos + new Vector3(-offset, -offset, 0f),  // 左下
                playerPos + new Vector3(offset, offset, 0f),    // 右上
                playerPos + new Vector3(offset, -offset, 0f)    // 右下
            };
            var ballObjects = new List<GameObject>();
            // 生成四个丝球
            foreach (var pos in positions)
            {
                var behavior = _silkBallManager.SpawnSilkBall(
                    pos,
                    acceleration: 15f,
                    maxSpeed: 20f,
                    chaseTime: 5f,
                    scale: 1f,
                    enableRotation: true
                );

                if (behavior != null)
                {
                    ballObjects.Add(behavior.gameObject);
                    behavior.gameObject.LocateMyFSM("Control").SendEvent("PREPARE");
                    behavior.isPrepared = true;
                    Log.Info($"在位置 {pos} 生成追踪丝球");
                }
            }

            yield return new WaitForSeconds(0.6f);

            // 释放所有丝球
            foreach (var ball in ballObjects)
            {
                ball.LocateMyFSM("Control").SendEvent("SILK BALL RELEASE");
            }

            yield return new WaitForSeconds(1f);
        }

        #endregion

        #region 清理方法

        /// <summary>
        /// 清理活跃的丝球列表（供BossBehavior调用）
        /// </summary>
        public void ClearActiveSilkBalls()
        {
            if (_activeSilkBalls != null && _activeSilkBalls.Count > 0)
            {
                Log.Info($"清理活跃丝球列表，当前数量: {_activeSilkBalls.Count}");
                _activeSilkBalls.Clear();
            }
        }

        /// <summary>
        /// 停止所有丝球相关的协程（供BossBehavior调用）
        /// </summary>
        public void StopAllSilkBallCoroutines()
        {
            // 停止丝球召唤协程
            if (_silkBallSummonCoroutine != null)
            {
                StopCoroutine(_silkBallSummonCoroutine);
                _silkBallSummonCoroutine = null;
                Log.Info("已停止丝球召唤协程");
            }
        }

        /// <summary>
        /// 在眩晕中断时，强制取消移动丝球并通知Control FSM（防止状态不同步）
        /// 这是关键的状态同步方法，确保两个FSM保持一致
        /// </summary>
        // public void OnStunInterruptDashState()
        // {
        //     Log.Info("眩晕中断移动丝球状态，执行强制取消以同步FSM状态");
            
        //     // 1. 停止生成
        //     StopGeneratingSilkBall();
            
        //     // 2. 清理活跃丝球列表
        //     ClearActiveSilkBalls();
            
        //     // 3. 通知Control FSM强制回到Idle（关键！防止状态不同步）
        //     if (_bossControlFsm != null)
        //     {
        //         _bossControlFsm.SendEvent("FORCE IDLE");
        //         Log.Info("眩晕中断：已发送FORCE IDLE事件给Control FSM，确保状态同步");
        //     }
        // }

        #endregion
    }
}