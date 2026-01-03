using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityObject = UnityEngine.Object;
using HazardType = GlobalEnums.HazardType;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;
namespace AnySilkBoss.Source.Behaviours.Normal;

/// <summary>
/// 通用Boss行为控制器基类
/// </summary>
[RequireComponent(typeof(tk2dSpriteAnimator))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayMakerFSM))]
internal partial class BossBehavior : MonoBehaviour
{
    public string fsmName = "Control";
    // 基础组件引用
    private PlayMakerFSM? _bossControlFsm;
    private AttackControlBehavior? _attackControlBehavior;

    private bool[] phaseFlags = new bool[10]; // 用于各阶段的标志位

    #region 移动丝球攻击常量

    // 区域坐标定义
    private const float DASH_SPEED = 8f; // 移动速度
    private const float DASH_ANTIC_TIME = 0.3f; // Dash准备动画时长（默认）
    private const float DASH_ANTIC_TIME_FIRST = 0.6f; // 第一次Dash准备动画时长（增加0.3秒）
    private const float IDLE_WAIT_TIME = 0.6f; // Idle等待时间（减少20%：从0.75到0.6）
    private const float MAX_DASH_TIME = 4f; // 最大移动时间（超时保护）

    // 六个区域的中心点坐标
    private static readonly Vector3 POS_LEFT_DOWN = new Vector3(25f, 137f, 0f);
    private static readonly Vector3 POS_LEFT_UP = new Vector3(25f, 142.5f, 0f);
    private static readonly Vector3 POS_MIDDLE_UP = new Vector3(39.5f, 142.5f, 0f);
    private static readonly Vector3 POS_MIDDLE_DOWN = new Vector3(39.5f, 137f, 0f);
    private static readonly Vector3 POS_RIGHT_UP = new Vector3(51.5f, 142.5f, 0f);
    private static readonly Vector3 POS_RIGHT_DOWN = new Vector3(51.5f, 137f, 0f);

    // Public Float变量供AttackControlBehavior访问（X和Y分开）
    public FsmFloat? RoutePoint0X;
    public FsmFloat? RoutePoint0Y;
    public FsmFloat? RoutePoint1X;
    public FsmFloat? RoutePoint1Y;
    public FsmFloat? RoutePoint2X;
    public FsmFloat? RoutePoint2Y;

    // ⚠️ Phase2 Special点位（左下或右下）
    public FsmFloat? RoutePointSpecialX;
    public FsmFloat? RoutePointSpecialY;

    // 时间变量
    public FsmFloat? IdleWaitTime;  // Idle等待时间

    private GameObject? _silkHair;

    // 隐形目标点GameObject（用于FaceObjectV2）
    private GameObject? _targetPoint0;
    private GameObject? _targetPoint1;
    private GameObject? _targetPoint2;
    private GameObject? _targetPointSpecial;  // Special点位目标

    // FsmGameObject变量（用于存储目标点引用）
    private FsmGameObject? _fsmTargetPoint0;
    private FsmGameObject? _fsmTargetPoint1;
    private FsmGameObject? _fsmTargetPoint2;
    private FsmGameObject? _fsmTargetPointSpecial;

    #endregion

    private void Awake()
    {
        // 初始化组件在Start中进行
    }

    private void Start()
    {
        StartCoroutine(DelayedSetup());
    }

    private void OnDestroy()
    {
        // 场景切换或对象销毁时清理所有丝球
        CleanupAllSilkBallsOnDestroy();
    }

    private void OnDisable()
    {
        // 对象禁用时也清理丝球
        CleanupAllSilkBallsOnDestroy();
    }

    private string _lastStateName = "";

    /// <summary>
    /// 延迟初始化
    /// </summary>
    private IEnumerator DelayedSetup()
    {
        yield return null;  // 等待一帧
        StartCoroutine(SetupBoss());
    }

    /// <summary>
    /// 设置Boss
    /// </summary>
    private IEnumerator SetupBoss()
    {
        GetComponents();
        ModifyBossControlFSM();
        Log.Info("Boss行为控制器初始化完成");
        yield return null;
    }

    /// <summary>
    /// 获取必要的组件
    /// </summary>
    private void GetComponents()
    {
        _bossControlFsm = FSMUtility.LocateMyFSM(base.gameObject, fsmName);
        _attackControlBehavior = GetComponent<AttackControlBehavior>();
        if (_attackControlBehavior == null)
        {
            Log.Warn("未找到AttackControlBehavior组件，部分眩晕清理逻辑将无效");
        }
        _silkHair = transform.parent.Find("Silk_Hair").gameObject;
    }


    

    #region 眩晕中断处理

    /// <summary>
    /// 添加眩晕时的丝球清理和中断逻辑
    /// </summary>
    private void AddStunInterruptHandling()
    {
        if (_bossControlFsm == null) return;

        Log.Info("=== 添加眩晕中断处理 ===");

        // 在 Stun Stagger 状态添加清理丝球的动作
        AddCleanupToStunStagger();

        // 在 Stun Recover 状态添加发送恢复事件的动作
        AddRecoveryEventToStunRecover();

        Log.Info("眩晕中断处理添加完成");
    }

    /// <summary>
    /// 在 Stun Stagger 状态添加清理丝球的逻辑
    /// </summary>
    private void AddCleanupToStunStagger()
    {
        var stunStaggerState = _bossControlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Stun Stagger");
        if (stunStaggerState == null)
        {
            Log.Warn("未找到 Stun Stagger 状态，跳过清理逻辑添加");
            return;
        }

        var actions = stunStaggerState.Actions.ToList();

        // 在状态开头添加清理丝球的调用
        actions.Insert(0, new CallMethod
        {
            behaviour = this,
            methodName = new FsmString("CleanupSilkBallsOnStun") { Value = "CleanupSilkBallsOnStun" },
            parameters = new FsmVar[0],
            everyFrame = false
        });


        stunStaggerState.Actions = actions.ToArray();
        Log.Info("已在 Stun Stagger 状态添加丝球清理、中断事件和状态同步");
    }

    /// <summary>
    /// 在 Stun Recover 状态添加恢复事件
    /// </summary>
    private void AddRecoveryEventToStunRecover()
    {
        var stunRecoverState = _bossControlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Stun Recover");
        if (stunRecoverState == null)
        {
            Log.Warn("未找到 Stun Recover 状态，跳过恢复事件添加");
            return;
        }

        var actions = stunRecoverState.Actions.ToList();

        // 在状态开头添加发送恢复事件（让卡住的Silk Ball状态可以转换）
        actions.Insert(0, new SendEventByName
        {
            eventTarget = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObjectFSM,
                excludeSelf = new FsmBool(false),
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
            },
            sendEvent = new FsmString("SILK BALL RECOVER") { Value = "SILK BALL RECOVER" },
            delay = new FsmFloat(0f),
            everyFrame = false
        });

        stunRecoverState.Actions = actions.ToArray();
        Log.Info("已在 Stun Recover 状态添加恢复事件发送");
    }

    /// <summary>
    /// 清理场景中的所有丝球（Boss眩晕时调用）
    /// </summary>
    public void CleanupSilkBallsOnStun()
    {
        Log.Info("Boss眩晕，开始清理丝球");

        // 停止移动丝球生成
        if (_attackControlBehavior != null)
        {
            _attackControlBehavior.StopGeneratingSilkBall();
            _attackControlBehavior.ClearActiveSilkBalls();
        }

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

    /// <summary>
    /// 场景切换或销毁时的完全清理（更彻底）
    /// </summary>
    private void CleanupAllSilkBallsOnDestroy()
    {
        Log.Info("场景切换/对象销毁，执行完全清理");

        // 停止所有协程
        StopAllCoroutines();

        // 停止移动丝球生成
        if (_attackControlBehavior != null)
        {
            _attackControlBehavior.StopGeneratingSilkBall();
            _attackControlBehavior.ClearActiveSilkBalls();
            _attackControlBehavior.StopAllSilkBallCoroutines();
        }

        // 通过SilkBallManager清理所有丝球实例
        var managerObj = GameObject.Find("AnySilkBossManager");
        if (managerObj != null)
        {
            var silkBallManager = managerObj.GetComponent<AnySilkBoss.Source.Managers.SilkBallManager>();
            if (silkBallManager != null)
            {
                silkBallManager.RecycleAllActiveSilkBalls();
                Log.Info("完全清理：已通过SilkBallManager回收所有丝球");
            }
        }
    }

    /// <summary>
    /// 眩晕时同步Attack Control FSM状态（防止两个FSM状态不同步）
    /// 这是关键方法，确保Attack Control能正确感知Control FSM的状态变化
    /// </summary>
    // public void SyncAttackControlStateOnStun()
    // {
    //     Log.Info("眩晕时同步Attack Control FSM状态");

    //     // 获取AttackControlBehavior
    //     if (_attackControlBehavior != null)
    //     {
    //         // 调用AttackControl的状态同步方法
    //         _attackControlBehavior.OnStunInterruptDashState();
    //         Log.Info("已调用AttackControl的状态同步方法");
    //     }
    //     else
    //     {
    //         Log.Warn("AttackControlBehavior为null，无法同步状态");
    //     }
    // }


    #endregion

    #region 爬升阶段漫游系统

    // 漫游相关变量
    private Vector3 _currentRoamTarget;
    private float _roamMoveStartTime;
    private bool _roamMoveComplete = false;

    /// <summary>
    /// 创建爬升阶段漫游状态链（含 Roar 状态）
    /// </summary>
    private void CreateClimbRoamStates()
    {
        if (_bossControlFsm == null) return;

        Log.Info("=== 开始创建爬升阶段状态链 ===");

        // 注册新事件
        RegisterClimbRoarEvents();

        // 使用 FsmStateBuilder 批量创建爬升阶段状态
        var climbStates = CreateStates(_bossControlFsm.Fsm,
            ("Climb Roar Prepare", "Boss吼叫准备"),
            ("Climb Roar", "Boss吼叫"),
            ("Climb Roar End", "Boss吼叫结束"),
            ("Climb Roar Done", "Boss吼叫完成"),
            ("Climb Roam Init", "漫游初始化"),
            ("Climb Roam Select Target", "选择漫游目标"),
            ("Climb Roam Move", "移动到目标"),
            ("Climb Roam Idle", "短暂停留")
        );
        AddStatesToFsm(_bossControlFsm, climbStates);

        var climbRoarPrepare = climbStates[0];
        var climbRoar = climbStates[1];
        var climbRoarEnd = climbStates[2];
        var climbRoarDone = climbStates[3];
        var climbRoamInit = climbStates[4];
        var climbRoamSelectTarget = climbStates[5];
        var climbRoamMove = climbStates[6];
        var climbRoamIdle = climbStates[7];

        // 找到Idle状态用于转换
        var idleState = FindState(_bossControlFsm, "Idle");

        // 添加 Roar 动作
        AddClimbRoarPrepareActions(climbRoarPrepare);
        AddClimbRoarActions(climbRoar);
        AddClimbRoarEndActions(climbRoarEnd);
        AddClimbRoarDoneActions(climbRoarDone);

        // 添加漫游动作
        AddClimbRoamInitActions(climbRoamInit);
        AddClimbRoamSelectTargetActions(climbRoamSelectTarget);
        AddClimbRoamMoveActions(climbRoamMove);
        AddClimbRoamIdleActions(climbRoamIdle);

        // 添加 Roar 转换
        AddClimbRoarTransitions(climbRoarPrepare, climbRoar, climbRoarEnd, climbRoarDone);

        // 添加漫游转换
        AddClimbRoamTransitions(climbRoamInit, climbRoamSelectTarget,
            climbRoamMove, climbRoamIdle);

        // 添加全局转换（包含 Roar 和 漫游）
        AddClimbPhaseGlobalTransitionsNew(climbRoarPrepare, climbRoamInit, idleState);
        
        // 重新初始化FSM
        // ReinitializeFsm(_bossControlFsm);
        ReinitializeFsmVariables(_bossControlFsm);
        Log.Info("=== 爬升阶段状态链创建完成 ===");
    }

    /// <summary>
    /// 注册 Climb Roar 事件
    /// </summary>
    private void RegisterClimbRoarEvents()
    {
        // 使用 FsmStateBuilder 批量注册事件
        RegisterEvents(_bossControlFsm!, "CLIMB ROAR START", "CLIMB ROAR DONE");
        Log.Info("Climb Roar 事件注册完成");
    }

    // 注：CreateClimbRoarXxxState 和 CreateClimbRoamXxxState 方法已被 CreateStates 批量创建替代

    private void AddClimbRoamInitActions(FsmState initState)
    {
        var actions = new List<FsmStateAction>();

        // 停止当前速度
        actions.Add(new SetVelocity2d
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            vector = new Vector2(0f, 0f),
            x = new FsmFloat { UseVariable = false },
            y = new FsmFloat(0f),
            everyFrame = false
        });

        // ⚠️ 禁用Boss碰撞器，防止爬升漫游时被地形挡住
        actions.Add(new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("DisableBossCollider") { Value = "DisableBossCollider" },
            parameters = new FsmVar[0]
        });

        // 初始化漫游参数
        actions.Add(new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("InitClimbRoamParameters") { Value = "InitClimbRoamParameters" },
            parameters = new FsmVar[0]
        });

        // 等待0.2秒
        actions.Add(new Wait
        {
            time = new FsmFloat(0.2f),
            finishEvent = FsmEvent.Finished
        });

        initState.Actions = actions.ToArray();
    }

    private void AddClimbRoamSelectTargetActions(FsmState selectState)
    {
        var actions = new List<FsmStateAction>();

        // 计算下一个漫游点
        actions.Add(new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("CalculateClimbRoamTarget") { Value = "CalculateClimbRoamTarget" },
            parameters = new FsmVar[0]
        });

        // 等待0.1秒
        actions.Add(new Wait
        {
            time = new FsmFloat(0.1f),
            finishEvent = FsmEvent.Finished
        });

        selectState.Actions = actions.ToArray();
    }

    private void AddClimbRoamMoveActions(FsmState moveState)
    {
        var actions = new List<FsmStateAction>();

        // 选择动画
        actions.Add(new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("SelectRoamAnimation") { Value = "SelectRoamAnimation" },
            parameters = new FsmVar[0]
        });

        // 每帧移动
        actions.Add(new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("MoveToRoamTarget") { Value = "MoveToRoamTarget" },
            parameters = new FsmVar[0],
            everyFrame = true
        });

        // 超时检测
        actions.Add(new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("CheckRoamMoveTimeout") { Value = "CheckRoamMoveTimeout" },
            parameters = new FsmVar[0],
            everyFrame = true
        });

        // ⚠️ 关键：添加一个长时间Wait来"锁住"状态，防止瞬发Action导致状态立即完成
        // 实际的完成由MoveToRoamTarget或CheckRoamMoveTimeout中的代码主动发送FINISHED事件
        // 这个Wait只是占位，不会真正等到999秒
        actions.Add(new Wait
        {
            time = new FsmFloat(999f),
            finishEvent = null  // 不设置finishEvent，依靠代码手动发送FINISHED
        });

        moveState.Actions = actions.ToArray();
    }

    private void AddClimbRoamIdleActions(FsmState idleState)
    {
        var actions = new List<FsmStateAction>();

        // 面向英雄
        if (HeroController.instance != null)
        {
            actions.Add(new FaceObjectV2
            {
                objectA = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                objectB = new FsmGameObject { Value = HeroController.instance.gameObject },
                spriteFacesRight = false,
                playNewAnimation = false,
                newAnimationClip = new FsmString("") { Value = "" },
                resetFrame = false,
                pauseBetweenTurns = 0.5f,
                everyFrame = false
            });
        }

        // 播放Idle动画
        actions.Add(new Tk2dPlayAnimation
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            animLibName = new FsmString("") { Value = "" },
            clipName = new FsmString("Idle") { Value = "Idle" }
        });

        // 等待1-2秒随机
        actions.Add(new WaitRandom
        {
            timeMin = new FsmFloat(1f),
            timeMax = new FsmFloat(2f),
            finishEvent = FsmEvent.Finished
        });

        idleState.Actions = actions.ToArray();
    }

    private void AddClimbRoamTransitions(FsmState initState, FsmState selectState,
        FsmState moveState, FsmState idleState)
    {
        // 使用 SetFinishedTransition 简化漫游状态转换
        SetFinishedTransition(initState, selectState);    // Init -> Select Target
        SetFinishedTransition(selectState, moveState);    // Select Target -> Move
        SetFinishedTransition(moveState, idleState);      // Move -> Idle
        SetFinishedTransition(idleState, selectState);    // Idle -> Select Target (循环)

        Log.Info("漫游状态转换设置完成");
    }

    /// <summary>
    /// 添加 Climb Roar Prepare 动作（清理 + 播放Roar动画，等待动画帧事件触发）
    /// 类似原版 Rerise Roar Antic
    /// </summary>
    private void AddClimbRoarPrepareActions(FsmState roarPrepareState)
    {
        var actions = new List<FsmStateAction>();

        // 1. 发送 ATTACK CLEAR 事件
        var attackClear = new SendEventToRegister
        {
            Fsm = _bossControlFsm!.Fsm,
            eventName = new FsmString("ATTACK CLEAR") { Value = "ATTACK CLEAR" }
        };
        actions.Add(attackClear);

        actions.Add(new SendEventByName
        {
            eventTarget = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObject,
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = _silkHair }
                }
            },
            sendEvent = "ROAR",
            delay = new FsmFloat(0f),
            everyFrame = false
        });

        // 4. 使用 Tk2dPlayAnimationWithEvents 播放 Roar 动画
        // 当动画中的帧事件触发时，会发送 animationTriggerEvent
        var roarAnim = new Tk2dPlayAnimationWithEvents
        {
            Fsm = _bossControlFsm.Fsm,
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            clipName = new FsmString("Roar") { Value = "Roar" },
            animationTriggerEvent = FsmEvent.Finished  // 动画帧事件触发FINISHED
        };
        actions.Add(roarAnim);

        roarPrepareState.Actions = actions.ToArray();
    }


    /// <summary>
    /// 添加 Climb Roar 动作（StartRoarEmitter + 监听动画完成 + 音效）
    /// 类似原版 Rerise Roar
    /// </summary>
    private void AddClimbRoarActions(FsmState climbRoarState)
    {
        var actions = new List<FsmStateAction>();
        var reriseRoarState = _bossControlFsm!.FsmStates.FirstOrDefault(x => x.Name == "Rerise Roar");

        if (reriseRoarState == null)
        {
            Log.Error("Could not find 'Rerise Roar' state in Boss Control FSM");
            return;
        }

        var originalEmitter = reriseRoarState.Actions.FirstOrDefault(x => x is StartRoarEmitter) as StartRoarEmitter;
        // 获取所有 PlayAudioEvent（有两个）
        var audioEvents = reriseRoarState.Actions.Where(x => x is PlayAudioEvent).Cast<PlayAudioEvent>().ToList();

        if (originalEmitter == null)
        {
            Log.Error("Missing StartRoarEmitter in 'Rerise Roar' state");
            return;
        }

        // 1. StartRoarEmitter
        var climbRoarEmitter = new StartRoarEmitter
        {
            Fsm = _bossControlFsm.Fsm,
            spawnPoint = originalEmitter.spawnPoint,
            delay = originalEmitter.delay,
            stunHero = new FsmBool(true) { Value = true },  // 让原版Roar机制处理玩家硬控
            roarBurst = originalEmitter.roarBurst,
            isSmall = originalEmitter.isSmall,
            noVisualEffect = originalEmitter.noVisualEffect,
            forceThroughBind = originalEmitter.forceThroughBind,
            stopOnExit = originalEmitter.stopOnExit
        };
        actions.Add(climbRoarEmitter);

        // 2. Tk2dWatchAnimationEvents（监听动画完成事件）
        var climbWatchAnim = new Tk2dWatchAnimationEvents
        {
            Fsm = _bossControlFsm.Fsm,
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            animationTriggerEvent = FsmEvent.Finished
        };
        actions.Add(climbWatchAnim);

        // 3. 复制所有 PlayAudioEvent
        foreach (var originalAudio in audioEvents)
        {
            var climbAudio = new PlayAudioEvent
            {
                Fsm = _bossControlFsm.Fsm,
                audioClip = originalAudio.audioClip,
                volume = originalAudio.volume,
                pitchMin = originalAudio.pitchMin,
                pitchMax = originalAudio.pitchMax,
                audioPlayerPrefab = originalAudio.audioPlayerPrefab,
                spawnPoint = originalAudio.spawnPoint,
                spawnPosition = originalAudio.spawnPosition,
                SpawnedPlayerRef = originalAudio.SpawnedPlayerRef
            };
            actions.Add(climbAudio);
        }

        climbRoarState.Actions = actions.ToArray();
        Log.Info($"Climb Roar 状态添加了 {actions.Count} 个 Actions（含 {audioEvents.Count} 个音效）");
    }

    /// <summary>
    /// 添加 Climb Roar End 动作（发送IDLE给Silk_Hair，等待）
    /// 类似原版 Rerise Roar End
    /// </summary>
    private void AddClimbRoarEndActions(FsmState roarEndState)
    {
        var actions = new List<FsmStateAction>();

        // 1. 发送 IDLE 给 Silk_Hair
        actions.Add(new SendEventByName
        {
            eventTarget = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObject,
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = _silkHair }
                }
            },
            sendEvent = "IDLE",
            delay = new FsmFloat(0f),
            everyFrame = false
        });

        actions.Add(new Tk2dWatchAnimationEvents
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            animationCompleteEvent = FsmEvent.Finished
        });

        roarEndState.Actions = actions.ToArray();
    }


    /// <summary>
    /// 添加 Climb Roar Done 动作（发送事件给PhaseControl并播放Idle）
    /// </summary>
    private void AddClimbRoarDoneActions(FsmState roarDoneState)
    {
        var actions = new List<FsmStateAction>();

        // 0. 播放二阶段音乐（原版在 Bound 2 状态中执行，现在移到这里）
        // 复制原版 Bound 2 的 ApplyMusicCue 行为
        var musicCue = Resources.FindObjectsOfTypeAll<MusicCue>()
            .FirstOrDefault(mc => mc.name == "Silk Boss B");
        if (musicCue != null)
        {
            actions.Add(new ApplyMusicCue
            {
                musicCue = new FsmObject { Value = musicCue },
                delayTime = new FsmFloat(0f),
                transitionTime = new FsmFloat(0f)
            });
            Log.Info("已添加二阶段音乐 ApplyMusicCue (Silk Boss B)");
        }
        else
        {
            Log.Warn("未找到 Silk Boss B MusicCue，二阶段音乐可能无法播放");
        }

        // 1. 发送 CLIMB ROAR DONE 给 Phase Control
        actions.Add(new SendEventByName
        {
            eventTarget = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObjectFSM,
                excludeSelf = new FsmBool(false),
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Phase Control") { Value = "Phase Control" }
            },
            sendEvent = new FsmString("CLIMB ROAR DONE") { Value = "CLIMB ROAR DONE" },
            delay = new FsmFloat(0f),
            everyFrame = false
        });

        // 2. 播放 Idle 动画
        actions.Add(new Tk2dPlayAnimation
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            clipName = new FsmString("Idle") { Value = "Idle" }
        });

        // 3. 延迟发送 BLADES RETURN
        // Finger Blade 的 Stagger 流程需要约 3.5 秒才能到达 Stagger Finish 状态
        // 在那里才能响应 BLADES RETURN 事件
        actions.Add(new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("SendBladesReturnDelayed") { Value = "SendBladesReturnDelayed" },
            parameters = new FsmVar[0],
            everyFrame = false
        });

        roarDoneState.Actions = actions.ToArray();
    }

    /// <summary>
    /// 延迟发送 BLADES RETURN 事件给所有 Finger Blade
    /// </summary>
    public void SendBladesReturnDelayed()
    {
        StartCoroutine(SendBladesReturnDelayedCoroutine());
    }

    private IEnumerator SendBladesReturnDelayedCoroutine()
    {
        // 等待 Finger Blade 完成 Stagger 流程到达 Stagger Finish
        // Stagger 流程：Stagger Pause(0-0.3s) → Stagger Anim(0.4-1s) → Stagger Drop(2.5s) → Stagger Finish
        // 总计最多约 3.8 秒，这里等待 3.5 秒应该足够
        yield return new WaitForSeconds(3.5f);

        // 第一次发送 BLADES RETURN
        var bladesReturnEvent = new SendEventToRegister
        {
            Fsm = _bossControlFsm!.Fsm,
            eventName = new FsmString("BLADES RETURN") { Value = "BLADES RETURN" }
        };
        bladesReturnEvent.OnEnter();
        Log.Info("延迟发送 BLADES RETURN（针对 Stagger Finish 状态）");

        // 再等待 1 秒，发送第二次（针对可能在 Rise 状态的 Finger）
        yield return new WaitForSeconds(1f);
        bladesReturnEvent.OnEnter();
        Log.Info("第二次发送 BLADES RETURN（针对 Rise 状态）");
    }

    /// <summary>
    /// 添加 Climb Roar 转换
    /// </summary>
    private void AddClimbRoarTransitions(FsmState roarPrepareState, FsmState roarState, FsmState roarEndState, FsmState roarDoneState)
    {
        // 使用 SetFinishedTransition 简化 Roar 状态转换
        SetFinishedTransition(roarPrepareState, roarState);   // Prepare -> Roar
        SetFinishedTransition(roarState, roarEndState);       // Roar -> End
        SetFinishedTransition(roarEndState, roarDoneState);   // End -> Done

        // Roar Done 不需要转换，等待 CLIMB PHASE START 全局事件
        roarDoneState.Transitions = Array.Empty<FsmTransition>();
    }

    /// <summary>
    /// 添加爬升阶段全局转换（新版，含Roar）
    /// </summary>
    private void AddClimbPhaseGlobalTransitionsNew(FsmState climbRoarPrepare, FsmState climbRoamInit, FsmState? idleState)
    {
        var globalTransitions = _bossControlFsm!.Fsm.GlobalTransitions.ToList();

        // 使用 CreateTransition 简化全局转换添加
        globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("CLIMB ROAR START"), climbRoarPrepare));
        globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("CLIMB PHASE START"), climbRoamInit));

        if (idleState != null)
        {
            globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("CLIMB PHASE END"), idleState));
        }

        _bossControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
        Log.Info("爬升阶段全局转换添加完成（含Roar）");
    }

    private void AddClimbPhaseGlobalTransitions(FsmState climbRoamInit, FsmState? idleState)
    {
        var globalTransitions = _bossControlFsm!.Fsm.GlobalTransitions.ToList();

        globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("CLIMB PHASE START"), climbRoamInit));

        if (idleState != null)
        {
            globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("CLIMB PHASE END"), idleState));
        }

        _bossControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
        Log.Info("爬升阶段全局转换添加完成");
    }

    /// <summary>
    /// 初始化漫游参数
    /// </summary>
    public void InitClimbRoamParameters()
    {
        // 漫游系统不需要特殊的初始化，参数都是硬编码的
        Log.Info("漫游参数初始化完成");
    }

    /// <summary>
    /// 计算漫游目标点
    /// </summary>
    public void CalculateClimbRoamTarget()
    {
        var hero = HeroController.instance;
        if (hero == null)
        {
            Log.Warn("HeroController 未找到，使用默认目标点");
            _currentRoamTarget = new Vector3(39.5f, 140f, 0f);
            return;
        }

        Vector3 playerPos = hero.transform.position;

        // 漫游边界 - 调整为玩家上方12-22单位
        float minX = 25f;
        float maxX = 55f;
        float minY = playerPos.y + 12f;  // 玩家上方12单位
        float maxY = 145f;                // 房间顶部

        // 随机选择目标（玩家上方附近）
        float targetX = Mathf.Clamp(
            playerPos.x + UnityEngine.Random.Range(-8f, 8f),
            minX,
            maxX
        );

        float targetY = Mathf.Clamp(
            playerPos.y + 17f + UnityEngine.Random.Range(-5f, 5f),
            minY,
            maxY
        );

        _currentRoamTarget = new Vector3(targetX, targetY, 0f);
        Log.Info($"选择漫游目标: {_currentRoamTarget}（玩家上方12-22单位）");
    }

    /// <summary>
    /// 选择漫游动画
    /// </summary>
    public void SelectRoamAnimation()
    {
        var bossPos = transform.position;
        bool movingForward = _currentRoamTarget.x > bossPos.x;

        var tk2dAnimator = GetComponent<tk2dSpriteAnimator>();
        if (tk2dAnimator != null)
        {
            tk2dAnimator.Play(movingForward ? "Drift F" : "Drift B");
        }

        _roamMoveStartTime = Time.time;
        _roamMoveComplete = false;

        Log.Info($"选择动画: {(movingForward ? "Drift F" : "Drift B")}");
    }

    /// <summary>
    /// 移动到漫游目标（每帧调用）
    /// </summary>
    public void MoveToRoamTarget()
    {
        if (_roamMoveComplete) return;

        Vector3 currentPos = transform.position;
        float distance = Vector3.Distance(currentPos, _currentRoamTarget);

        // 到达检测（容差0.5单位）
        if (distance < 0.5f)
        {
            _roamMoveComplete = true;
            if (_bossControlFsm != null)
            {
                _bossControlFsm.SendEvent("FINISHED");
                Log.Info("到达漫游目标");
            }
            return;
        }
        float moveSpeed = 9f;  // 爬升漫游速度（提升50%: 6f -> 9f）
        Vector3 direction = (_currentRoamTarget - currentPos).normalized;
        transform.position += direction * moveSpeed * Time.deltaTime;
    }

    /// <summary>
    /// 检查漫游移动超时
    /// </summary>
    public void CheckRoamMoveTimeout()
    {
        if (_roamMoveComplete) return;

        if (Time.time - _roamMoveStartTime > 3f)
        {
            Log.Warn($"漫游移动超时（3秒），强制完成。当前位置: {transform.position}，目标: {_currentRoamTarget}");
            _roamMoveComplete = true;
            if (_bossControlFsm != null)
            {
                _bossControlFsm.SendEvent("FINISHED");
            }
        }
    }

    /// <summary>
    /// 在Idle状态检测Dash Pending，并根据Special Attack决定走哪条路径
    /// </summary>
    public void CheckAndTriggerDashPath()
    {
        if (_bossControlFsm == null) return;

        var dashPendingVar = _bossControlFsm.FsmVariables.FindFsmBool("Silk Ball Dash Pending");
        if (dashPendingVar == null || !dashPendingVar.Value) return;

        var specialAttackVar = _bossControlFsm.FsmVariables.FindFsmBool("Special Attack");
        bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

        // 重置Pending标志
        dashPendingVar.Value = false;

        // 根据Phase2决定触发哪个事件
        if (isPhase2)
        {
            _bossControlFsm.SendEvent("SILK BALL DASH SPECIAL BRIDGE");
            Log.Info("触发Phase2 Special路径");
        }
        else
        {
            _bossControlFsm.SendEvent("SILK BALL DASH BRIDGE");
            Log.Info("触发普通Dash路径");
        }
    }

    #endregion

    #region 爬升阶段修复

    /// <summary>
    /// 初始化爬升Cast保护
    /// </summary>
    private void InitializeClimbCastProtection()
    {
        var controlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
        if (controlFsm == null) return;

        // 创建Climb Cast Prepare状态
        CreateClimbCastPrepareState();

        // 重新初始化FSM
        controlFsm.Fsm.InitData();

        Log.Info("爬升Cast保护初始化完成");
    }

    /// <summary>
    /// 创建爬升Cast保护状态（在Control FSM中）
    /// 用于保护长时间的Cast动画
    /// </summary>
    private void CreateClimbCastPrepareState()
    {
        var controlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
        if (controlFsm == null) return;

        // 检查是否已存在
        if (StateExists(controlFsm, "Climb Cast Prepare"))
        {
            Log.Info("Climb Cast Prepare状态已存在，跳过创建");
            return;
        }

        // 使用 FsmStateBuilder 创建并添加状态
        var climbCastPrepareState = CreateAndAddState(controlFsm, "Climb Cast Prepare", "爬升Cast动画保护状态（长Wait时间）");

        var actions = new List<FsmStateAction>();

        var climbCastPendingVar = EnsureBoolVariable(controlFsm, "Climb Cast Pending");
        actions.Add(new SetBoolValue
        {
            boolVariable = climbCastPendingVar,
            boolValue = new FsmBool(false)
        });

        // 添加Wait动作
        actions.Add(new Wait
        {
            time = new FsmFloat(2.5f),
            finishEvent = FsmEvent.Finished,
            realTime = false
        });

        climbCastPrepareState.Actions = actions.ToArray();

        // 添加转换：FINISHED → Idle
        var idleState = FindState(controlFsm, "Idle");
        if (idleState != null)
        {
            SetFinishedTransition(climbCastPrepareState, idleState);
        }

        Log.Info("创建Climb Cast Prepare状态完成");
    }

    private void SetupControlIdlePendingTransitions()
    {
        if (_bossControlFsm == null)
        {
            Log.Warn("_bossControlFsm未初始化，无法配置Idle布尔检测");
            return;
        }

        var idleState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
        if (idleState == null)
        {
            Log.Warn("未找到Idle状态，无法配置布尔检测");
            return;
        }

        var climbCastState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Climb Cast Prepare");
        var dashAntic0State = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Dash Antic 0");
        var dashAnticSpecialState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Dash Antic Special");

        var climbPendingVar = EnsureBoolVariable(_bossControlFsm, "Climb Cast Pending");
        var dashPendingVar = EnsureBoolVariable(_bossControlFsm, "Silk Ball Dash Pending");
        var specialAttackVar = EnsureBoolVariable(_bossControlFsm, "Special Attack");

        var actions = idleState.Actions?.ToList() ?? new List<FsmStateAction>();
        actions.RemoveAll(action => action is BoolTest boolTest &&
            (boolTest.boolVariable == climbPendingVar || boolTest.boolVariable == dashPendingVar));

        var climbBridgeEvent = FsmEvent.GetFsmEvent("CLIMB CAST BRIDGE");
        var dashBridgeEvent = FsmEvent.GetFsmEvent("SILK BALL DASH BRIDGE");
        var dashSpecialBridgeEvent = FsmEvent.GetFsmEvent("SILK BALL DASH SPECIAL BRIDGE");

        // ⚠️ 添加一个CallMethod来动态判断应该走哪条路径
        if (dashAnticSpecialState != null && dashAntic0State != null)
        {
            actions.Insert(2, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("CheckAndTriggerDashPath") { Value = "CheckAndTriggerDashPath" },
                parameters = new FsmVar[0],
                everyFrame = true
            });
        }
        else if (dashAntic0State != null)
        {
            // 只有普通路径
            actions.Insert(2, new BoolTest
            {
                boolVariable = dashPendingVar,
                isTrue = dashBridgeEvent,
                isFalse = FsmEvent.GetFsmEvent("NULL"),
                everyFrame = true
            });
        }

        if (climbCastState != null)
        {
            actions.Insert(2, new BoolTest
            {
                boolVariable = climbPendingVar,
                isTrue = climbBridgeEvent,
                isFalse = FsmEvent.GetFsmEvent("NULL"),
                everyFrame = true
            });
        }

        idleState.Actions = actions.ToArray();

        var transitions = idleState.Transitions?.ToList() ?? new List<FsmTransition>();
        transitions.RemoveAll(t => t.FsmEvent == climbBridgeEvent || t.FsmEvent == dashBridgeEvent || t.FsmEvent == dashSpecialBridgeEvent);

        if (climbCastState != null)
        {
            transitions.Add(new FsmTransition
            {
                FsmEvent = climbBridgeEvent,
                toState = climbCastState.Name,
                toFsmState = climbCastState
            });
        }

        if (dashAnticSpecialState != null)
        {
            transitions.Add(new FsmTransition
            {
                FsmEvent = dashSpecialBridgeEvent,
                toState = dashAnticSpecialState.Name,
                toFsmState = dashAnticSpecialState
            });
        }

        if (dashAntic0State != null)
        {
            transitions.Add(new FsmTransition
            {
                FsmEvent = dashBridgeEvent,
                toState = dashAntic0State.Name,
                toFsmState = dashAntic0State
            });
        }

        idleState.Transitions = transitions.ToArray();



        Log.Info("Idle状态布尔检测配置完成");
    }

    private FsmBool EnsureBoolVariable(PlayMakerFSM targetFsm, string variableName)
    {
        var boolVars = targetFsm.FsmVariables.BoolVariables.ToList();
        var existing = boolVars.FirstOrDefault(v => v.Name == variableName);
        if (existing != null)
        {
            return existing;
        }

        var newVar = new FsmBool(variableName) { Value = false };
        boolVars.Add(newVar);
        targetFsm.FsmVariables.BoolVariables = boolVars.ToArray();
        targetFsm.FsmVariables.Init();
        Log.Info($"创建Control FSM Bool变量: {variableName}");
        return newVar;
    }

    #endregion

    #region 爬升阶段碰撞控制

    // 保存原始碰撞器状态
    private bool _collider2DWasEnabled = true;
    private Collider2D? _bossCollider2D;

    /// <summary>
    /// 禁用Boss的Collider2D（爬升漫游阶段使用，防止被地形挡住）
    /// </summary>
    public void DisableBossCollider()
    {
        if (_bossCollider2D == null)
        {
            _bossCollider2D = GetComponent<Collider2D>();
        }

        if (_bossCollider2D != null)
        {
            _collider2DWasEnabled = _bossCollider2D.enabled;
            _bossCollider2D.enabled = false;
            Log.Info("已禁用Boss Collider2D（爬升漫游阶段）");
        }
        else
        {
            Log.Warn("未找到Boss Collider2D组件");
        }
    }

    /// <summary>
    /// 恢复Boss的Collider2D（Boss返回场地后调用）
    /// </summary>
    public void EnableBossCollider()
    {
        if (_bossCollider2D != null)
        {
            _bossCollider2D.enabled = _collider2DWasEnabled;
            Log.Info("已恢复Boss Collider2D");
        }
    }

    #endregion
}
