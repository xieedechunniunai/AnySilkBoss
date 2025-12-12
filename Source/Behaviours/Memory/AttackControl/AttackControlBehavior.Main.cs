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
        protected PlayMakerFSM? _attackControlFsm;

        /// <summary>
        /// 公共属性：Attack Control FSM 是否已初始化
        /// </summary>
        public bool IsAttackControlFsmReady => _attackControlFsm != null;

        // 状态引用
        protected FsmState? _handPtnChoiceState;
        protected FsmState? _orbitAttackState;
        protected FsmState? _waitForHandsReadyState;
        protected string _secondHandName = "";
        protected GameObject? _bossScene;
        protected GameObject? _strandPatterns;
        protected GameObject? _secondHandObject;

        // 事件引用缓存
        protected FsmEvent? _orbitAttackEvent;
        protected FsmEvent? _orbitStartHandLEvent;
        protected FsmEvent? _orbitStartHandREvent;
        protected FsmEvent? _nullEvent;

        // 丝球环绕攻击相关
        protected FsmEvent? _silkBallAttackEvent;
        protected SilkBallManager? _silkBallManager;
        protected List<GameObject> _activeSilkBalls = new List<GameObject>();
        protected Coroutine? _silkBallSummonCoroutine;

        // 丝球攻击状态引用
        protected FsmState? _silkBallPrepareState;
        protected FsmState? _silkBallPrepareCastState;
        protected FsmState? _silkBallCastState;
        protected FsmState? _silkBallLiftState;
        protected FsmState? _silkBallAnticState;
        protected FsmState? _silkBallReleaseState;
        protected FsmState? _silkBallEndState;
        protected FsmState? _silkBallRecoverState;

        // 移动丝球攻击状态引用
        protected FsmState? _silkBallDashPrepareState;
        protected FsmState? _silkBallDashEndState;

        // BossControl FSM引用（用于通信）
        protected PlayMakerFSM? _bossControlFsm;

        // 移动丝球相关变量（AttackControl中）
        protected FsmBool? _isGeneratingSilkBall;  // 是否正在生成丝球
        protected FsmFloat? _totalDistanceTraveled; // 累计移动距离
        protected FsmVector2? _lastBallPosition;    // 上次生成丝球的位置

        protected FsmGameObject? _laceSlashObj;
        protected FsmGameObject? _spikeFloorsX;
        protected FsmEvent? _silkBallStaticEvent;
        protected FsmEvent? _silkBallDashEvent;
        protected FsmEvent? _silkBallDashStartEvent;
        protected FsmEvent? _silkBallDashEndEvent;

        // P6 Web攻击相关事件与状态
        protected FsmEvent? _p6WebAttackEvent;
        protected FsmState? _p6WebPrepareState;
        protected FsmState? _p6WebCastState;
        protected FsmState? _p6WebAttack1State;
        protected FsmState? _p6WebAttack2State;
        protected FsmState? _p6WebAttack3State;
        protected FsmState? _p6WebRecoverState;

        // 丝球释放时的冲击波和音效动作缓存
        protected StartRoarEmitter? _cachedRoarEmitter;
        protected PlayAudioEventRandom? _cachedPlayRoarAudio;

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

            _bossScene = transform.GetParent().gameObject;
            if (_bossScene == null)
            {
                Log.Error("未找到bossScene");
                return;
            }

            _bossControlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_bossControlFsm == null)
            {
                Log.Error("未找到 BossControl FSM");
            }

            _strandPatterns = _bossScene.transform.Find("Strand Patterns").gameObject;
            InitializeSilkBallDashVariables();

            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _silkBallManager = managerObj.GetComponent<SilkBallManager>();
                if (_silkBallManager == null)
                {
                    Log.Warn("未找到 SilkBallManager 组件");
                }

                var assetManager = managerObj.GetComponent<AssetManager>();
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

            InitializeHandBehaviors();
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
            CreateClimbPhaseAttackStates();
            CreateP6WebAttackStates();
            ModifyRubbleAttackForP6Web();
            ModifySendRandomEventAction();
            ModifyAttackChoiceForSilkBall();
            PatchOriginalAttackPatterns();
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
                "SILK BALL ATTACK",
                "SILK BALL STATIC",
                "SILK BALL DASH",
                "SILK BALL DASH START",
                "SILK BALL DASH END",
                "NULL",
                "CLIMB PHASE ATTACK",
                "CLIMB PHASE END",
                "P6 WEB ATTACK"
            );

            _orbitAttackEvent = FsmEvent.GetFsmEvent("ORBIT ATTACK");
            _orbitStartHandLEvent = FsmEvent.GetFsmEvent("ORBIT START Hand L");
            _orbitStartHandREvent = FsmEvent.GetFsmEvent("ORBIT START Hand R");
            _silkBallAttackEvent = FsmEvent.GetFsmEvent("SILK BALL ATTACK");
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
        protected T? CloneAction<T>(string sourceStateName, int matchIndex = 0, Func<T, bool>? predicate = null) where T : FsmStateAction
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

