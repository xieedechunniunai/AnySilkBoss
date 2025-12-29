using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;
using System;
using System.Reflection;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 普通版 Attack Control 行为（主模块：字段与通用初始化）
    /// </summary>
    internal partial class MemoryAttackControlBehavior : MonoBehaviour
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

        public MemoryHandControlBehavior? handLBehavior;
        public MemoryHandControlBehavior? handRBehavior;

        // FSM引用
        private PlayMakerFSM? _attackControlFsm;

        /// <summary>
        /// 公共属性：Attack Control FSM 是否已初始化
        /// </summary>
        public bool IsAttackControlFsmReady => _attackControlFsm != null;

        // 状态引用
        private FsmState? _handPtnChoiceState;
        private FsmState? _orbitAttackState;
        private FsmState? _waitForHandsReadyState;
        private FsmState? _attackChoiceState;
        private FsmState? _moveRestartState;
        private FsmState? _moveRestart3State;
        private FsmState? _idleState;
        private FsmState? _webRecoverState;
        private FsmState? _singleState;
        private FsmState? _doubleState;
        private FsmState? _rubbleAttackQuestionState;
        private FsmState? _attackStopState;
        private FsmState? _dashAttackState;
        private FsmState? _dashAttackAnticState;
        private FsmState? _dashAttackEndState;
        private FsmState? _spikeLiftAimState;
        private FsmState? _spikeLiftAim2State;
        private string _secondHandName = "";
        private GameObject? _bossScene;
        private GameObject? _strandPatterns;
        private GameObject? _secondHandObject;
        private GameObject? _silkHair;
        private GameObject? _naChargeEffect;

        // 事件引用缓存
        private FsmEvent? _orbitAttackEvent;
        private FsmEvent? _orbitStartHandLEvent;
        private FsmEvent? _orbitStartHandREvent;
        private FsmEvent? _nullEvent;

        // 丝球环绕攻击相关

        // BossControl FSM引用（用于通信）
        private PlayMakerFSM? _bossControlFsm;

        // FSM 内部变量缓存（从 Attack Control FSM 获取 Hand L/R）
        private FsmGameObject? _handLFsmVar;
        private FsmGameObject? _handRFsmVar;
        private PlayMakerFSM? _handLControlFsm;
        private PlayMakerFSM? _handRControlFsm;

        // P6 Web攻击相关事件与状态（领域次元斩）
        private FsmEvent? _p6WebAttackEvent;
        private FsmState? _p6DomainSlashState;
        private DomainBehavior? _domainBehavior;

        // 丝球释放时的冲击波和音效动作缓存
        // 坐标常量
        private static readonly Vector3 POS_LEFT_DOWN = new Vector3(25f, 137f, 0f);
        private static readonly Vector3 POS_LEFT_UP = new Vector3(25f, 142.5f, 0f);
        private static readonly Vector3 POS_MIDDLE_UP = new Vector3(39.5f, 142.5f, 0f);
        private static readonly Vector3 POS_MIDDLE_DOWN = new Vector3(39.5f, 137f, 0f);
        private static readonly Vector3 POS_RIGHT_UP = new Vector3(51.5f, 142.5f, 0f);
        private static readonly Vector3 POS_RIGHT_DOWN = new Vector3(51.5f, 137f, 0f);

        private const float ZONE_LEFT_MAX = 31f;
        private const float ZONE_RIGHT_MIN = 46f;
        private const float ATTACK_SEND_DELAY = 0.6f;
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

            // 检查是否应该停止生成丝球（如果不在移动丝球相关状态）
            if (_isGeneratingSilkBall != null && _isGeneratingSilkBall.Value && _attackControlFsm != null)
            {
                var currentStateName = _attackControlFsm.ActiveStateName;
                // 如果不在移动丝球准备状态，立即停止生成（防止状态被意外打断后继续生成）
                if (currentStateName != "Silk Ball Move Prepare")
                {
                    Log.Info($"检测到不在移动丝球准备状态（当前状态：{currentStateName}），停止生成丝球");
                    StopGeneratingSilkBall();
                }
                else
                {
                    CheckAndSpawnSilkBall();
                }
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

            Log.Info("=== Attack Control FSM 信息 ===");
            Log.Info($"FSM名称: {_attackControlFsm.FsmName}");
            Log.Info($"当前状态: {_attackControlFsm.ActiveStateName}");
            FsmAnalyzer.WriteFsmReport(_attackControlFsm, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\_attackControlFsm.txt");
        }

        private IEnumerator DelayedSetup()
        {
            yield return null;
            yield return new WaitWhile(() => FSMUtility.LocateMyFSM(gameObject, fsmName) == null);

            GetComponents();
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

            // 从 FSM 变量获取 Hand L/R
            _handLFsmVar = _attackControlFsm.FsmVariables.FindFsmGameObject("Hand L");
            _handRFsmVar = _attackControlFsm.FsmVariables.FindFsmGameObject("Hand R");
            handL = _handLFsmVar?.Value;
            handR = _handRFsmVar?.Value;

            // 获取 Hand Control FSM（注意名称是 "Hand Control" 不是 "Control"）
            _handLControlFsm = handL != null ? FSMUtility.LocateMyFSM(handL, "Hand Control") : null;
            _handRControlFsm = handR != null ? FSMUtility.LocateMyFSM(handR, "Hand Control") : null;

            if (_handLControlFsm == null)
            {
                Log.Warn("未找到 Hand L 的 Hand Control FSM");
            }
            if (_handRControlFsm == null)
            {
                Log.Warn("未找到 Hand R 的 Hand Control FSM");
            }

            CacheOriginalStates();

            var parent = transform.parent;
            if (parent == null)
            {
                Log.Error("未找到bossScene");
                return;
            }
            _bossScene = parent.gameObject;

            _silkHair = parent.Find("Silk_Hair")?.gameObject;
            if (_silkHair == null)
            {
                Log.Warn("未找到 Silk_Hair 物体（可能名称不同或不存在）");
            }

            _bossControlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_bossControlFsm == null)
            {
                Log.Error("未找到 BossControl FSM");
            }

            _strandPatterns = _bossScene.transform.Find("Strand Patterns").gameObject;
            InitializeSilkBallDashVariables();

            InitializeNaChargeEffect();

            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _silkBallManager = managerObj.GetComponent<SilkBallManager>();
                if (_silkBallManager == null)
                {
                    Log.Warn("未找到 SilkBallManager 组件");
                }

                _singleWebManager = managerObj.GetComponent<SingleWebManager>();
                if (_singleWebManager == null)
                {
                    Log.Warn("未找到 SingleWebManager 组件");
                }

                // 初始化DomainBehavior（领域结界）
                var domainObj = new GameObject("DomainBehavior");
                domainObj.transform.SetParent(managerObj.transform);
                _domainBehavior = domainObj.AddComponent<DomainBehavior>();

                var assetManager = managerObj.GetComponent<AssetManager>();
                if (assetManager != null)
                {
                    var originalLaceCircleSlash = assetManager.Get<GameObject>("lace_circle_slash");
                    if (originalLaceCircleSlash != null)
                    {
                        // 复制一份，不修改原始资源
                        laceCircleSlash = GameObject.Instantiate(originalLaceCircleSlash);
                        laceCircleSlash.name = "LaceCircleSlash_Copy";
                        laceCircleSlash.SetActive(false);

                        // 设置大小为两倍
                        laceCircleSlash.transform.localScale = originalLaceCircleSlash.transform.localScale * 2f;

                        // 添加或启用 AutoRecycleSelf 组件
                        var autoRecycle = laceCircleSlash.GetComponent<AutoRecycleSelf>();
                        if (autoRecycle == null)
                        {
                            autoRecycle = laceCircleSlash.AddComponent<AutoRecycleSelf>();
                        }
                        if (autoRecycle != null)
                        {
                            autoRecycle.enabled = true;
                        }

                        Log.Info("已创建 laceCircleSlash 副本，设置大小为两倍并启用 AutoRecycleSelf");
                    }
                    else
                    {
                        Log.Warn("从 AssetManager 获取的 lace_circle_slash 为 null");
                    }
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

            InitializeHandBehaviors();
            InitializeSilkBallReleaseActions();
            InitializeSpikeSystem();
        }

        private void InitializeNaChargeEffect()
        {
            if (_bossScene == null) return;
            if (_naChargeEffect != null) return;

            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("未找到 HeroController，无法初始化 NA Charge 特效");
                return;
            }

            var effects = hero.transform.Find("Effects");
            if (effects == null)
            {
                Log.Warn("未找到 Hero/Effects，无法初始化 NA Charge 特效");
                return;
            }

            Transform? naCharge = effects.Find("NA Charge");
            if (naCharge == null)
            {
                Log.Warn("未找到 NA Charge（Hero/Effects 下），无法初始化特效");
                return;
            }

            var copy = Instantiate(naCharge.gameObject, gameObject.transform);
            copy.name = "NA Charge (Boss)";
            copy.transform.localPosition = new Vector3(0, 4.3f, 0);
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = 3 * Vector3.one;
            copy.SetActive(false);
            _naChargeEffect = copy;
        }
        private void CacheOriginalStates()
        {
            if (_attackControlFsm == null) return;

            _attackChoiceState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Attack Choice");
            _moveRestartState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Move Restart");
            _moveRestart3State = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Move Restart 3");
            _idleState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Idle");
            _webRecoverState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Web Recover");
            _singleState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Single");
            _doubleState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Double");
            _rubbleAttackQuestionState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Rubble Attack?");

            _attackStopState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Attack Stop");
            _dashAttackState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Dash Attack");
            _dashAttackAnticState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Dash Attack Antic");
            _dashAttackEndState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Dash Attack End");
            _spikeLiftAimState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Spike Lift Aim");
            _spikeLiftAim2State = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == "Spike Lift Aim 2");
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

            _cachedRoarEmitter = CloneAction<StartRoarEmitter>("Roar");
            if (_cachedRoarEmitter != null)
            {
                _cachedRoarEmitter.Fsm = _attackControlFsm.Fsm;
                _cachedRoarEmitter.Owner = _attackControlFsm.gameObject;
            }

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

            _handPtnChoiceState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == handPtnChoiceState);
            if (_handPtnChoiceState == null)
            {
                Log.Error($"未找到状态: {handPtnChoiceState}");
                return;
            }

            _waitForHandsReadyState = _attackControlFsm.FsmStates.FirstOrDefault(state => state.Name == waitForHandsReadyState);
            if (_waitForHandsReadyState == null)
            {
                Log.Error($"未找到状态: {waitForHandsReadyState}");
                return;
            }

            CreateOrbitAttackState();
            CreateSilkBallAttackStates();
            CreateSilkBallWithWebAttackStates();
            CreateClimbPhaseAttackStates();
            CreateP6WebAttackStates();
            
            // 注意：不再在这里预热 SingleWebManager 对象池
            // 池子容量已在 SingleWebManager 中设置为 MEMORY_MIN_POOL_SIZE = 70
            
            InitializeBlastBurstAttacks();

            ModifyRubbleAttackForP6Web();
            ModifySendRandomEventAction();
            ModifyAttackChoiceForSilkBall();
            PatchOriginalAttackPatterns();
            PatchDashOrbitForDashAttack();
            AddAttactStopAction();
            ModifySpikeLiftAimState();
            RelinkAllEventReferences();
            Log.Info("Attack Control FSM修改完成");
        }

        /// <summary>
        /// 注册Attack Control FSM的所有事件
        /// </summary>
        private void RegisterAttackControlEvents()
        {
            if (_attackControlFsm == null) return;

            RegisterEvents(_attackControlFsm,
                "ORBIT ATTACK",
                "ORBIT START Hand L",
                "ORBIT START Hand R",
                "DASH ORBIT START Hand L",
                "DASH ORBIT START Hand R",
                "DASH ORBIT SHOOT Hand L",
                "DASH ORBIT SHOOT Hand R",
                "DASH HANDS READY",
                "SILK BALL ATTACK",
                "SILK BALL WITH WEB ATTACK",
                "SILK BALL WITH WEB ATTACK DONE",
                "SILK BALL STATIC",
                "SILK BALL DASH",
                "SILK BALL DASH START",
                "SILK BALL DASH END",
                "NULL",
                "CLIMB PHASE ATTACK",
                "CLIMB PHASE END",
                "P6 WEB ATTACK",
                "P6 DOMAIN SLASH DONE"
            );

            _orbitAttackEvent = FsmEvent.GetFsmEvent("ORBIT ATTACK");
            _orbitStartHandLEvent = FsmEvent.GetFsmEvent("ORBIT START Hand L");
            _orbitStartHandREvent = FsmEvent.GetFsmEvent("ORBIT START Hand R");
            _silkBallAttackEvent = FsmEvent.GetFsmEvent("SILK BALL ATTACK");
            _silkBallWithWebAttackEvent = FsmEvent.GetFsmEvent("SILK BALL WITH WEB ATTACK");
            _silkBallWithWebAttackDoneEvent = FsmEvent.GetFsmEvent("SILK BALL WITH WEB ATTACK DONE");
            _silkBallStaticEvent = FsmEvent.GetFsmEvent("SILK BALL STATIC");
            _silkBallDashEvent = FsmEvent.GetFsmEvent("SILK BALL DASH");
            _silkBallDashStartEvent = FsmEvent.GetFsmEvent("SILK BALL DASH START");
            _silkBallDashEndEvent = FsmEvent.GetFsmEvent("SILK BALL DASH END");
            _nullEvent = FsmEvent.GetFsmEvent("NULL");
            _p6WebAttackEvent = FsmEvent.GetFsmEvent("P6 WEB ATTACK");
        }

        /// <summary>
        /// 重新链接所有事件引用
        /// </summary>
        private void RelinkAllEventReferences()
        {
            if (_attackControlFsm == null) return;
            ReinitializeFsmVariables(_attackControlFsm);
        }
        #endregion

        #region 辅助方法
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

        #region 清理方法
        public void ClearActiveSilkBalls()
        {
            if (_activeSilkBalls != null && _activeSilkBalls.Count > 0)
            {
                Log.Info($"清理活跃丝球列表，当前数量: {_activeSilkBalls.Count}");
                _activeSilkBalls.Clear();
            }
        }

        public void StopAllSilkBallCoroutines()
        {
            if (_silkBallSummonCoroutine != null)
            {
                StopCoroutine(_silkBallSummonCoroutine);
                _silkBallSummonCoroutine = null;
                Log.Info("已停止丝球召唤协程");
            }

            if (_silkBallWithWebAttackCoroutine != null)
            {
                StopCoroutine(_silkBallWithWebAttackCoroutine);
                _silkBallWithWebAttackCoroutine = null;
                Log.Info("已停止丝球+丝线协程");
            }

            if (_naChargeEffect != null)
            {
                _naChargeEffect.SetActive(false);
            }
        }
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
        #endregion
    }
}

