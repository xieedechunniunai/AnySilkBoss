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
    internal partial class AttackControlBehavior : MonoBehaviour
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
        public GameObject? laceCircleSlash;

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
        private FsmState? _silkBallPrepareCastState;
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

        // 移动丝球相关变量（AttackControl中）
        private FsmBool? _isGeneratingSilkBall;  // 是否正在生成丝球
        private FsmFloat? _totalDistanceTraveled; // 累计移动距离
        private FsmVector2? _lastBallPosition;    // 上次生成丝球的位置

        private FsmGameObject? _laceSlashObj;
        // 移动丝球相关事件
        private FsmEvent? _silkBallStaticEvent;
        private FsmEvent? _silkBallDashEvent;
        private FsmEvent? _silkBallDashStartEvent;
        private FsmEvent? _silkBallDashEndEvent;

        // 眩晕中断相关事件
        private FsmEvent? _silkBallInterruptEvent;
        private FsmEvent? _silkBallRecoverEvent;

        // P6 Web攻击相关事件
        private FsmEvent? _p6WebAttackEvent;

        // P6 Web攻击状态引用
        private FsmState? _p6WebPrepareState;
        private FsmState? _p6WebCastState;
        private FsmState? _p6WebAttack1State;
        private FsmState? _p6WebAttack2State;
        private FsmState? _p6WebAttack3State;
        private FsmState? _p6WebRecoverState;

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
            FsmAnalyzer.WriteFsmReport(_attackControlFsm, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\_attackControlFsm.txt");
        }

        private IEnumerator DelayedSetup()
        {
            yield return null; // 等待一帧，确保FSM已初始化

            // 等待FSM初始化
            yield return new WaitWhile(() => FSMUtility.LocateMyFSM(gameObject, fsmName) == null);

            GetComponents();
            // 首先注册所有需要的事件
            RegisterAttackControlEvents();
            ModifyAttackControlFSM();
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
            _bossControlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_bossControlFsm == null)
            {
                Log.Error("未找到 BossControl FSM");
            }

            // // 获取BossBehavior组件
            // _bossBehavior = GetComponent<BossBehavior>();
            // if (_bossBehavior == null)
            // {
            //     Log.Error("未找到 BossBehavior 组件");
            // }
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
                var assetManager = managerObj.GetComponent<Managers.AssetManager>();
                if (assetManager != null)
                {
                    laceCircleSlash = assetManager.Get<GameObject>("lace_circle_slash");
                    ModifyDashAttackState();
                }
                else
                {
                    Log.Warn("未找到 AssetManager 组件");
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

            // 创建P6 Web攻击状态链
            CreateP6WebAttackStates();

            // 修改Rubble Attack?状态，添加P6 Web攻击监听
            ModifyRubbleAttackForP6Web();

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
            var existingEvents = _attackControlFsm.Fsm.Events.ToList();

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

            // P6 Web Attack事件
            _p6WebAttackEvent = FsmEvent.GetFsmEvent("P6 WEB ATTACK");
            if (_p6WebAttackEvent != null && !existingEvents.Contains(_p6WebAttackEvent))
            {
                existingEvents.Add(_p6WebAttackEvent);
            }

            _attackControlFsm.Fsm.Events = existingEvents.ToArray();
        }

        /// <summary>
        /// 重新链接所有事件引用
        /// </summary>
        private void RelinkAllEventReferences()
        {
            if (_attackControlFsm == null) return;

            // 重新初始化FSM数据，确保所有事件引用正确
            _attackControlFsm.Fsm.InitData();
            _attackControlFsm.Fsm.InitEvents();
            _attackControlFsm.FsmVariables.Init();
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

            if (handL != null)
            {
                handLBehavior = handL.GetComponent<HandControlBehavior>();
                if (handLBehavior == null)
                {
                    handLBehavior = handL.AddComponent<HandControlBehavior>();
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
                    handRBehavior = handR.AddComponent<HandControlBehavior>();
                }
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
        }

        /// <summary>
        /// 添加环绕攻击状态的转换
        /// </summary>
        private void AddOrbitAttackTransitions(FsmState orbitFirstShootState)
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
                toState = "Orbit First Shoot",
                toFsmState = orbitFirstShootState
            };

            _orbitAttackState!.Transitions = new FsmTransition[] { finishedTransition };
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

        #region 丝球攻击

        /// <summary>
        /// 创建丝球环绕攻击的所有状态
        /// </summary>
        private void CreateSilkBallAttackStates()
        {

            // 创建所有状态（静态版本 + 移动版本）
            _silkBallPrepareState = CreateSilkBallPrepareState();
            _silkBallPrepareCastState = CreateSilkBallPrepareCastState();
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
            states.Add(_silkBallPrepareCastState);
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
                    toState = "Silk Ball Prepare Cast",
                    toFsmState = _silkBallPrepareCastState
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
            _silkBallPrepareCastState!.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Silk Ball Cast",
                    toFsmState = _silkBallCastState
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

            }

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
            // 按顺序查找所有SendRandomEventV4动作（保持它们在Actions数组中的顺序）
            var sendRandomActions = new List<SendRandomEventV4>();
            for (int i = 0; i < attackChoiceState.Actions.Length; i++)
            {
                if (attackChoiceState.Actions[i] is SendRandomEventV4 sendRandomAction)
                {
                    sendRandomActions.Add(sendRandomAction);
                }
            }

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

            }
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

            // ⚠️ 创建Special Attack变量（Phase2特殊攻击标志）
            var specialAttack = _attackControlFsm.FsmVariables.FindFsmBool("Special Attack");
            if (specialAttack == null)
            {
                specialAttack = new FsmBool("Special Attack") { Value = false };
                var bools = _attackControlFsm.FsmVariables.BoolVariables.ToList();
                bools.Add(specialAttack);
                _attackControlFsm.FsmVariables.BoolVariables = bools.ToArray();
            }
            var p6WebAttack = _attackControlFsm.FsmVariables.FindFsmBool("Do P6 Web Attack");
            if (p6WebAttack == null)
            {
                p6WebAttack = new FsmBool("Do P6 Web Attack") { Value = false };
                var bools = _attackControlFsm.FsmVariables.BoolVariables.ToList();
                bools.Add(p6WebAttack);
                _attackControlFsm.FsmVariables.BoolVariables = bools.ToArray();
            }
            _laceSlashObj = _attackControlFsm.FsmVariables.FindFsmGameObject("Lace Slash Obj");
            if (_laceSlashObj == null || _laceSlashObj.Value == null)
            {
                _laceSlashObj = new FsmGameObject("Lace Slash Obj") { Value = laceCircleSlash };
                var objects = _attackControlFsm.FsmVariables.GameObjectVariables.ToList();
                objects.Add(_laceSlashObj);
                _attackControlFsm.FsmVariables.GameObjectVariables = objects.ToArray();
            }
            _attackControlFsm.FsmVariables.Init();
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
            // 3. 设置Control FSM变量，交由Idle状态布尔检测跳转
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                fsm = _bossControlFsm,
                variableName = new FsmString("Silk Ball Dash Pending") { Value = "Silk Ball Dash Pending" },
                setValue = new FsmBool(true),
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



            // 4. 等待BossControl发回的SILK BALL DASH END事件（在Transitions中处理）
            // 添加超时保护：10秒后如果还没收到DASH END事件，强制转移（防止卡死）
            actions.Add(new Wait
            {
                time = new FsmFloat(10f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();
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
            }
        }

        /// <summary>
        /// 延迟释放移动丝球
        /// 等待丝球进入已准备状态（Prepare 状态执行完，MarkAsPrepared 被调用）后再发送 RELEASE，
        /// 避免在丝球仍处于 Init/Idle 时过早发送事件导致状态卡死
        /// </summary>
        private IEnumerator DelayedReleaseSilkBallForDash(GameObject silkBall)
        {
            if (silkBall == null)
            {
                yield break;
            }

            var behavior = silkBall.GetComponent<SilkBallBehavior>();

            // 等待丝球完成 Prepare（MarkAsPrepared 会把 isPrepared 置为 true）
            float waited = 0f;
            const float maxWait = 0.5f;
            while (behavior != null && !behavior.isPrepared && waited < maxWait)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            var controlFsm = silkBall.LocateMyFSM("Control");
            if (controlFsm != null)
            {
                controlFsm.SendEvent("SILK BALL RELEASE");
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

            // ⚠️ 检查是否为Phase2
            var specialAttackVar = _attackControlFsm?.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            // 判断Boss区域
            BossZone zone = GetBossZone(bossPos.x);

            Vector3 point0, point1, point2;
            Vector3? pointSpecial = null;

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

            // ⚠️ Phase2时设置Special点位：Boss 在左侧 -> 右下；Boss 在右侧 -> 左下；
            // 如果在中间，则根据玩家相对位置选择对侧下角
            if (isPhase2)
            {
                if (zone == BossZone.Left)
                {
                    pointSpecial = POS_RIGHT_DOWN;
                    Log.Info("Phase2模式：Boss在左侧，Special点位 = 右下");
                }
                else if (zone == BossZone.Right)
                {
                    pointSpecial = POS_LEFT_DOWN;
                    Log.Info("Phase2模式：Boss在右侧，Special点位 = 左下");
                }
                else
                {
                    // 中区：以 Hero 的位置作为参考，仍然选择对侧下角
                    var hero = HeroController.instance;
                    if (hero != null && hero.transform.position.x < bossPos.x)
                    {
                        // Hero 在左侧 -> 冲向右下
                        pointSpecial = POS_RIGHT_DOWN;
                        Log.Info("Phase2模式：Boss在中区，Hero在左侧，Special点位 = 右下");
                    }
                    else
                    {
                        // Hero 在右侧或未找到 Hero -> 冲向左下
                        pointSpecial = POS_LEFT_DOWN;
                        Log.Info("Phase2模式：Boss在中区，Hero在右侧或未找到Hero，Special点位 = 左下");
                    }
                }
            }

            // 设置到BossControl的Float变量（X和Y分开）
            if (isPhase2 && pointSpecial.HasValue)
            {
                SetRoutePointSpecial(pointSpecial.Value);
            }
            SetRoutePoint(0, point0);
            SetRoutePoint(1, point1);
            SetRoutePoint(2, point2);

            // 同时更新隐形目标点GameObject的位置
            var bossBehavior = gameObject.GetComponent<BossBehavior>();
            if (bossBehavior != null)
            {
                bossBehavior.UpdateTargetPointPositions(point0, point1, point2, pointSpecial);
            }
            else
            {
                Log.Warn("未找到BossBehavior组件，无法更新隐形目标点位置");
            }

            string routeLog = isPhase2 && pointSpecial.HasValue
                ? $"Special({pointSpecial.Value}) → {point0} → {point1} → {point2}"
                : $"{point0} → {point1} → {point2}";
            Log.Info($"路线已设置: {routeLog}");
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
        /// 设置Special路线点位（Phase2专用）
        /// </summary>
        private void SetRoutePointSpecial(Vector3 value)
        {
            var bossBehavior = gameObject.GetComponent<BossBehavior>();
            if (bossBehavior == null)
            {
                Log.Error("未找到BossBehavior组件，无法设置Special路线点");
                return;
            }

            if (bossBehavior.RoutePointSpecialX != null && bossBehavior.RoutePointSpecialY != null)
            {
                bossBehavior.RoutePointSpecialX.Value = value.x;
                bossBehavior.RoutePointSpecialY.Value = value.y;
                Log.Info($"设置Special路线点: X={value.x}, Y={value.y}");
            }
            else
            {
                Log.Error("RoutePointSpecialX 或 RoutePointSpecialY 为 null");
            }
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
        /// 召唤丝球的协程（普通版8个，Phase2版12个双圈）
        /// </summary>
        public IEnumerator SummonSilkBallsAtHighPointCoroutine()
        {
            // 检查是否为Phase2
            var specialAttackVar = _attackControlFsm?.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            if (isPhase2)
            {
                yield return SummonPhase2DoubleSilkBalls();
            }
            else
            {
                yield return SummonNormalSilkBalls();
            }
        }

        /// <summary>
        /// 普通版：召唤8个丝球
        /// </summary>
        private IEnumerator SummonNormalSilkBalls()
        {
            Log.Info("=== 开始召唤普通版8个丝球 ===");
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
                spawnPosition.z = 0f;
                // 召唤丝球
                var behavior = _silkBallManager?.SpawnSilkBall(spawnPosition, 30f, 25f, 8f, 1f, true);
                if (behavior != null)
                {
                    _activeSilkBalls.Add(behavior.gameObject);
                    Log.Info($"召唤第 {i + 1} 个丝球");
                }

                // 等待0.1秒后再召唤下一个
                yield return new WaitForSeconds(0.1f);
            }

            Log.Info($"=== 8个丝球召唤完成，共 {_activeSilkBalls.Count} 个 ===");
        }

        /// <summary>
        /// Phase2版：召唤12个丝球（内圈6个+外圈6个，角度互补）
        /// </summary>
        private IEnumerator SummonPhase2DoubleSilkBalls()
        {
            Log.Info("=== 开始召唤Phase2双圈12个丝球 ===");
            _activeSilkBalls.Clear();
            Vector3 bossPosition = transform.position;
            float innerRadius = 6f;   // 内圈半径
            float outerRadius = 14f;  // 外圈半径

            // 每次同时生成内外圈各一个，共循环6次
            for (int i = 0; i < 6; i++)
            {
                // 外圈：30°, 90°, 150°, 210°, 270°, 330° (逆时针)
                float outerAngle = 30f + i * 60f;
                float outerRadians = outerAngle * Mathf.Deg2Rad;
                Vector3 outerOffset = new Vector3(
                    Mathf.Cos(outerRadians) * outerRadius,
                    Mathf.Sin(outerRadians) * outerRadius,
                    0f
                );
                Vector3 outerSpawnPosition = bossPosition + outerOffset;
                outerSpawnPosition.z = 0f;
                var outerBehavior = _silkBallManager?.SpawnSilkBall(outerSpawnPosition, 30f, 25f, 8f, 1f, true);
                if (outerBehavior != null)
                {
                    _activeSilkBalls.Add(outerBehavior.gameObject);
                    // 外圈丝球2.5s内不会被墙体摧毁
                    outerBehavior.StartProtectionTime(2.5f);
                    Log.Info($"召唤第 {i + 1} 对：外圈丝球（{outerAngle}°）");
                }

                // 内圈：0°, 60°, 120°, 180°, 240°, 300° (顺时针)
                float innerAngle = i * 60f;
                float innerRadians = innerAngle * Mathf.Deg2Rad;
                Vector3 innerOffset = new Vector3(
                    Mathf.Cos(innerRadians) * innerRadius,
                    Mathf.Sin(innerRadians) * innerRadius,
                    0f
                );
                Vector3 innerSpawnPosition = bossPosition + innerOffset;
                innerSpawnPosition.z = 0f;
                var innerBehavior = _silkBallManager?.SpawnSilkBall(innerSpawnPosition, 30f, 25f, 8f, 1f, true);
                if (innerBehavior != null)
                {
                    _activeSilkBalls.Add(innerBehavior.gameObject);
                    Log.Info($"召唤第 {i + 1} 对：内圈丝球（{innerAngle}°）");
                }

                yield return new WaitForSeconds(0.12f);
            }

            Log.Info($"=== Phase2双圈12个丝球召唤完成，共 {_activeSilkBalls.Count} 个 ===");
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
        private FsmState CreateSilkBallPrepareCastState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "Silk Ball Prepare Cast",
                Description = "丝球攻击Prepare Cast"
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
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                fsm = _bossControlFsm,
                variableName = new FsmString("Attack Prepare") { Value = "Attack Prepare" },
                setValue = new FsmBool(true),
                everyFrame = false
            });
            state.Actions = actions.ToArray();

            return state;
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


        #region 原版AttackControl调整
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
            // 获取状态的所有 Actions
            var dashAttackactions = dashAttackAnticState.Actions.ToList();            // 遍历并修改目标 SendEventByName 行为
            foreach (var action in dashAttackactions)
            {
                if (action is SendEventByName sendEventByName &&
                    sendEventByName.sendEvent?.Value != null &&
                    sendEventByName.sendEvent.Value.Contains("STOMP DASH"))
                {
                    sendEventByName.delay = new FsmFloat(0.28f);
                }
            }
            // 将修改后的 Actions 重新赋值回状态
            dashAttackAnticState.Actions = dashAttackactions.ToArray();
            var dashAttackEndState = _attackControlFsm?.FsmStates.FirstOrDefault(x => x.Name == "Dash Attack End");
            if (dashAttackEndState == null) { return; }
            // 获取状态的所有 Actions
            var dashAttackEndactions = dashAttackEndState.Actions.ToList();

            // 创建 SpawnObjectFromGlobalPool Action 来生成 Lace Circle Slash
            if (laceCircleSlash != null)
            {
                dashAttackEndactions.Insert(0, new SpawnObjectFromGlobalPool
                {
                    gameObject = new FsmGameObject { Value = laceCircleSlash },
                    spawnPoint = new FsmGameObject { Value = this.gameObject },
                    position = new FsmVector3 { Value = Vector3.zero },
                    rotation = new FsmVector3 { Value = Vector3.zero },
                    storeObject = _laceSlashObj
                });
            }
            else
            {
                Log.Warn("laceCircleSlash 为 null，跳过 Dash Attack End 的斩击特效生成");
            }
            // 将修改后的 Actions 重新赋值回状态
            dashAttackEndState.Actions = dashAttackEndactions.ToArray();
        }


        private void ClearSilkBallMethod()
        {
            Log.Info("Boss眩晕，开始清理丝球");
            StopGeneratingSilkBall();
            ClearActiveSilkBalls();
            // 通过SilkBallManager清理所有丝球实例
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                var silkBallManager = managerObj.GetComponent<Managers.SilkBallManager>();
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
        }

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

            // 在ExecuteClimbSilkBallAttack之前：设置Control FSM变量，交由Idle状态布尔检测跳转
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                fsm = _bossControlFsm,
                variableName = new FsmString("Climb Cast Pending") { Value = "Climb Cast Pending" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            // 执行丝球攻击
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbSilkBallAttack") { Value = "ExecuteClimbSilkBallAttack" },
                parameters = new FsmVar[0]
            });

            // 修改Wait时间为2.0秒（覆盖1.6秒的协程）
            actions.Add(new Wait
            {
                time = new FsmFloat(2.0f),
                finishEvent = FsmEvent.Finished
            });

            silkBallState.Actions = actions.ToArray();
        }

        private void AddClimbAttackCooldownActions(FsmState cooldownState)
        {
            var actions = new List<FsmStateAction>();

            // 等待2-3秒冷却
            actions.Add(new WaitRandom
            {
                timeMin = new FsmFloat(2f),
                timeMax = new FsmFloat(3f),
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
            HandControlBehavior? selectedHand = handIndex == 0 ? handLBehavior : handRBehavior;

            if (selectedHand == null)
            {
                Log.Error($"未找到Hand {handIndex} Behavior");
                yield break;
            }

            Log.Info($"选择Hand {handIndex} ({selectedHand.gameObject.name})进行爬升阶段环绕攻击（削弱版）");

            // ⚠️ 直接调用HandControlBehavior的环绕攻击完整流程
            // 1. 启动环绕攻击序列（三根针开始环绕）
            selectedHand.StartOrbitAttackSequence();

            // 2. 等待环绕2秒
            yield return new WaitForSeconds(2f);

            // 3. 启动SHOOT序列（会自动按顺序发射三根针，每0.5秒一根）
            selectedHand.StartShootSequence();

            Log.Info("爬升阶段环绕攻击完成（单Hand三根针）");
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

        #region Phase2特殊攻击配置
        /// <summary>
        /// 配置环绕攻击参数（根据Special Attack状态）
        /// </summary>
        public void ConfigureOrbitAttack()
        {
            if (_attackControlFsm == null) return;

            // 检查是否启用Special Attack
            var specialAttackVar = _attackControlFsm.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            if (isPhase2)
            {
                // Phase2配置：正反旋转，发射间隔0.4秒
                if (handLBehavior != null)
                {
                    handLBehavior.SetOrbitAttackConfig(1f, 0.4f);  // 顺时针
                }
                if (handRBehavior != null)
                {
                    handRBehavior.SetOrbitAttackConfig(-1f, 0.4f); // 逆时针
                }
                Log.Info("Phase2环绕攻击配置：Hand L顺时针，Hand R逆时针，间隔0.4秒");
            }
            else
            {
                // 普通配置：同向旋转，发射间隔0.5秒
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
        #endregion
        #endregion
    }
}