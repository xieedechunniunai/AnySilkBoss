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
namespace AnySilkBoss.Source.Behaviours;

/// <summary>
/// 通用Boss行为控制器基类
/// </summary>
[RequireComponent(typeof(tk2dSpriteAnimator))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayMakerFSM))]
internal class BossBehavior : MonoBehaviour
{
    public string fsmName = "Control";
    // 基础组件引用
    private PlayMakerFSM? _bossControlFsm;
    private AttackControlBehavior? _attackControlBehavior;

    protected bool[] phaseFlags = new bool[10]; // 用于各阶段的标志位

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

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T) && _bossControlFsm != null)
        {
            Log.Info($"Boss FSM 信息: {_bossControlFsm.FsmName}");
            FsmAnalyzer.WriteFsmReport(_bossControlFsm, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\_bossControlFsm.txt");
        }
    }

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
    protected virtual IEnumerator SetupBoss()
    {
        GetComponents();
        ModifyBossControlFSM();
        Log.Info("Boss行为控制器初始化完成");
        yield return null;
    }

    /// <summary>
    /// 获取必要的组件
    /// </summary>
    protected virtual void GetComponents()
    {
        _bossControlFsm = FSMUtility.LocateMyFSM(base.gameObject, fsmName);
        _attackControlBehavior = GetComponent<AttackControlBehavior>();
        if (_attackControlBehavior == null)
        {
            Log.Warn("未找到AttackControlBehavior组件，部分眩晕清理逻辑将无效");
        }
        _silkHair = transform.parent.Find("Silk_Hair").gameObject;
    }


    #region 移动丝球攻击状态创建

    /// <summary>
    /// 修改BossControl FSM，添加移动丝球攻击状态
    /// </summary>
    private void ModifyBossControlFSM()
    {
        if (_bossControlFsm == null)
        {
            Log.Error("BossControl FSM未初始化，无法修改");
            return;
        }

        Log.Info("修改Boss Control FSM");

        // 创建移动丝球攻击状态链
        CreateSilkBallDashStates();
        // ModifyOriginFsm();

        // 添加大丝球大招锁定状态
        CreateBigSilkBallLockState();

        // 添加爬升阶段漫游状态
        CreateClimbRoamStates();

        // 添加全局事件监听
        AddGlobalEventListeners();

        // 添加眩晕时的丝球清理逻辑
        AddStunInterruptHandling();

        // 添加爬升阶段修复
        InitializeClimbCastProtection();

        SetupControlIdlePendingTransitions();
        
        ReinitializeFsmVariables(_bossControlFsm);
        Log.Info("Boss Control FSM修改完成");
    }

    /// <summary>
    /// 创建移动丝球攻击的状态链
    /// </summary>
    private void CreateSilkBallDashStates()
    {
        if (_bossControlFsm == null) return;

        Log.Info("=== 开始创建移动丝球攻击状态链 ===");


        // 2. 创建隐形目标点GameObject
        CreateInvisibleTargetPoints();

        // 3. 初始化Route Point变量（X和Y分开）
        RoutePoint0X = new FsmFloat("Route Point 0 X") { Value = 0f };
        RoutePoint0Y = new FsmFloat("Route Point 0 Y") { Value = 0f };
        RoutePoint1X = new FsmFloat("Route Point 1 X") { Value = 0f };
        RoutePoint1Y = new FsmFloat("Route Point 1 Y") { Value = 0f };
        RoutePoint2X = new FsmFloat("Route Point 2 X") { Value = 0f };
        RoutePoint2Y = new FsmFloat("Route Point 2 Y") { Value = 0f };

        // ⚠️ Phase2 Special点位
        RoutePointSpecialX = new FsmFloat("Route Point Special X") { Value = 0f };
        RoutePointSpecialY = new FsmFloat("Route Point Special Y") { Value = 0f };

        // 4. 初始化时间变量
        IdleWaitTime = new FsmFloat("Idle Wait Time") { Value = IDLE_WAIT_TIME };

        // 5. 初始化FsmGameObject变量（指向隐形目标点）
        _fsmTargetPoint0 = new FsmGameObject("Target Point 0") { Value = _targetPoint0 };
        _fsmTargetPoint1 = new FsmGameObject("Target Point 1") { Value = _targetPoint1 };
        _fsmTargetPoint2 = new FsmGameObject("Target Point 2") { Value = _targetPoint2 };
        _fsmTargetPointSpecial = new FsmGameObject("Target Point Special") { Value = _targetPointSpecial };

        // 5. 添加到FSM变量列表
        var floatVars = _bossControlFsm.FsmVariables.FloatVariables.ToList();
        floatVars.Add(RoutePoint0X);
        floatVars.Add(RoutePoint0Y);
        floatVars.Add(RoutePoint1X);
        floatVars.Add(RoutePoint1Y);
        floatVars.Add(RoutePoint2X);
        floatVars.Add(RoutePoint2Y);
        floatVars.Add(RoutePointSpecialX);
        floatVars.Add(RoutePointSpecialY);
        floatVars.Add(IdleWaitTime);
        _bossControlFsm.FsmVariables.FloatVariables = floatVars.ToArray();

        var gameObjectVars = _bossControlFsm.FsmVariables.GameObjectVariables.ToList();
        gameObjectVars.Add(_fsmTargetPoint0);
        gameObjectVars.Add(_fsmTargetPoint1);
        gameObjectVars.Add(_fsmTargetPoint2);
        gameObjectVars.Add(_fsmTargetPointSpecial);
        _bossControlFsm.FsmVariables.GameObjectVariables = gameObjectVars.ToArray();

        // ⚠️ 创建Special Attack变量（Phase2特殊攻击标志）
        EnsureBoolVariable(_bossControlFsm, "Special Attack");

        Log.Info("Route Point变量（包括Special）、时间变量、目标点GameObject变量和Special Attack变量已创建");

        // 创建所有状态（包括Special路径）
        var dashAnticSpecialState = CreateDashAnticState(-1);  // -1表示Special
        var dashToSpecialState = CreateDashToPointState(-1);
        var idleAtSpecialState = CreateIdleAtPointState(-1);

        var dashAntic0State = CreateDashAnticState(0);
        var dashToPoint0State = CreateDashToPointState(0);
        var idleAtPoint0State = CreateIdleAtPointState(0);

        var dashAntic1State = CreateDashAnticState(1);
        var dashToPoint1State = CreateDashToPointState(1);
        var idleAtPoint1State = CreateIdleAtPointState(1);

        var dashAntic2State = CreateDashAnticState(2);
        var dashToPoint2State = CreateDashToPointState(2);
        var dashEndState = CreateSilkBallDashEndState();

        // 使用 FsmStateBuilder 批量添加状态
        AddStatesToFsm(_bossControlFsm,
            dashAnticSpecialState, dashToSpecialState, idleAtSpecialState,
            dashAntic0State, dashToPoint0State, idleAtPoint0State,
            dashAntic1State, dashToPoint1State, idleAtPoint1State,
            dashAntic2State, dashToPoint2State, dashEndState);

        // 设置转换（包括Special路径）
        SetupSilkBallDashTransitions(
            dashAnticSpecialState, dashToSpecialState, idleAtSpecialState,
            dashAntic0State, dashToPoint0State, idleAtPoint0State,
            dashAntic1State, dashToPoint1State, idleAtPoint1State,
            dashAntic2State, dashToPoint2State,
            dashEndState);

        // 初始化FSM
        // ReinitializeFsm(_bossControlFsm);

        Log.Info("移动丝球状态链创建完成");
    }

    /// <summary>
    /// 设置移动丝球状态链的转换关系（包括Special路径）
    /// </summary>
    private void SetupSilkBallDashTransitions(
        FsmState dashAnticSpecial, FsmState dashToSpecial, FsmState idleAtSpecial,
        FsmState dashAntic0, FsmState dashToPoint0, FsmState idleAtPoint0,
        FsmState dashAntic1, FsmState dashToPoint1, FsmState idleAtPoint1,
        FsmState dashAntic2, FsmState dashToPoint2,
        FsmState dashEnd)
    {
        // 使用 SetFinishedTransition 简化所有 FINISHED 转换

        // Special路径（Phase2才使用）
        SetFinishedTransition(dashAnticSpecial, dashToSpecial);
        SetFinishedTransition(dashToSpecial, idleAtSpecial);
        SetFinishedTransition(idleAtSpecial, dashAntic0);

        // Point 0
        SetFinishedTransition(dashAntic0, dashToPoint0);
        SetFinishedTransition(dashToPoint0, idleAtPoint0);
        SetFinishedTransition(idleAtPoint0, dashAntic1);

        // Point 1
        SetFinishedTransition(dashAntic1, dashToPoint1);
        SetFinishedTransition(dashToPoint1, idleAtPoint1);
        SetFinishedTransition(idleAtPoint1, dashAntic2);

        // Point 2
        SetFinishedTransition(dashAntic2, dashToPoint2);
        SetFinishedTransition(dashToPoint2, dashEnd);

        // Dash End -> Idle
        var idleState = FindState(_bossControlFsm!, "Idle");
        if (idleState != null)
        {
            SetFinishedTransition(dashEnd, idleState);
        }

        Log.Info("移动丝球状态链转换设置完成（包括Special路径）");
    }

    /// <summary>
    /// 创建Dash准备状态
    /// </summary>
    private FsmState CreateDashAnticState(int pointIndex)
    {
        string stateName = pointIndex == -1 ? "Dash Antic Special" : $"Dash Antic {pointIndex}";
        string description = pointIndex == -1 ? "准备冲刺到Special点位" : $"准备冲刺到点位 {pointIndex}";

        var state = new FsmState(_bossControlFsm!.Fsm)
        {
            Name = stateName,
            Description = description
        };

        var actions = new List<FsmStateAction>();

        // 获取目标GameObject和坐标
        FsmGameObject? targetObject = null;

        switch (pointIndex)
        {
            case -1:  // Special点位
                targetObject = _fsmTargetPointSpecial;
                break;
            case 0:
                targetObject = _fsmTargetPoint0;
                break;
            case 1:
                targetObject = _fsmTargetPoint1;
                break;
            case 2:
                targetObject = _fsmTargetPoint2;
                break;
        }

        if (targetObject == null)
        {
            Log.Error($"CreateDashAnticState: Target object for point {pointIndex} is null.");
            return state;
        }
        // 2. 面向目标点
        actions.Add(new FaceObjectV2
        {
            objectA = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            objectB = targetObject,
            spriteFacesRight = false,
            playNewAnimation = false,
            resetFrame = false,
            pauseBetweenTurns = 0,
            everyFrame = false
        });

        // 3. 播放Dash Antic动画
        actions.Add(new Tk2dPlayAnimationWithEvents
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            clipName = new FsmString("Dash Antic") { Value = "Dash Antic" },
            animationCompleteEvent = null // 不需要完成事件
        });

        // 4. 等待准备时间（第一次Antic使用更长时间）
        float anticTime = pointIndex == 0 ? DASH_ANTIC_TIME_FIRST : DASH_ANTIC_TIME;
        actions.Add(new Wait
        {
            time = anticTime,
            finishEvent = FsmEvent.Finished,
            realTime = false
        });

        if (pointIndex == 0 && _bossControlFsm != null)
        {
            var dashPendingVar = EnsureBoolVariable(_bossControlFsm, "Silk Ball Dash Pending");
            actions.Insert(0, new SetBoolValue
            {
                boolVariable = dashPendingVar,
                boolValue = new FsmBool(false)
            });
        }

        state.Actions = actions.ToArray();
        return state;
    }

    /// <summary>
    /// 创建大丝球大招锁定状态
    /// </summary>
    private void CreateBigSilkBallLockState()
    {
        if (_bossControlFsm == null) return;

        Log.Info("创建大丝球大招锁定状态");

        // 使用 FsmStateBuilder 创建并添加锁定状态
        var lockState = CreateAndAddState(_bossControlFsm, "Big Silk Ball Lock", "大丝球大招期间锁定BOSS，只播放Idle动画");
        lockState.Actions = Array.Empty<FsmStateAction>();

        // 添加转换：只监听BIG SILK BALL UNLOCK事件
        var idleState = FindState(_bossControlFsm, "Idle");
        if (idleState != null)
        {
            lockState.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("BIG SILK BALL UNLOCK"), idleState)
            };
        }

        // 重新初始化FSM
        // ReinitializeFsm(_bossControlFsm);

        Log.Info("大丝球大招锁定状态创建完成");
    }

    /// <summary>
    /// 添加全局事件监听（在任意状态都可以响应）
    /// </summary>
    private void AddGlobalEventListeners()
    {
        if (_bossControlFsm == null) return;

        var idleState = FindState(_bossControlFsm, "Idle");
        if (idleState == null)
        {
            Log.Error("未找到Idle状态，跳过添加FORCE IDLE全局监听");
            return;
        }

        var lockState = FindState(_bossControlFsm, "Big Silk Ball Lock");
        if (lockState == null)
        {
            Log.Warn("未找到Big Silk Ball Lock状态，跳过添加BIG SILK BALL LOCK全局监听");
        }

        // 添加全局转换
        var globalTransitions = _bossControlFsm.FsmGlobalTransitions.ToList();

        // 添加FORCE IDLE全局转换
        globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("FORCE IDLE"), idleState));

        // 添加BIG SILK BALL LOCK全局转换
        if (lockState != null)
        {
            globalTransitions.Add(CreateTransition(FsmEvent.GetFsmEvent("BIG SILK BALL LOCK"), lockState));
        }
        _bossControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
    }

    /// <summary>
    /// 创建Dash到指定点位的状态（使用FaceObjectV2面向目标，Tk2dPlayAnimationWithEvents播放Dash动画）
    /// </summary>
    private FsmState CreateDashToPointState(int pointIndex)
    {
        string stateName = pointIndex == -1 ? "Dash To Special" : $"Dash To Point {pointIndex}";
        string description = pointIndex == -1 ? "冲刺到Special点位" : $"冲刺到点位 {pointIndex}";

        var state = new FsmState(_bossControlFsm!.Fsm)
        {
            Name = stateName,
            Description = description
        };

        var actions = new List<FsmStateAction>();

        // 获取目标坐标
        FsmFloat? targetX = null;
        FsmFloat? targetY = null;

        switch (pointIndex)
        {
            case -1:  // Special点位
                targetX = RoutePointSpecialX;
                targetY = RoutePointSpecialY;
                break;
            case 0:
                targetX = RoutePoint0X;
                targetY = RoutePoint0Y;
                // Point 0 需要取消硬直
                actions.Add(new SendEventByName
                {
                    eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObject,
                        excludeSelf = new FsmBool(false),
                        gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner }
                    },
                    sendEvent = new FsmString("STUN CONTROL STOP") { Value = "STUN CONTROL STOP" },
                    delay = new FsmFloat(0f),
                    everyFrame = false
                });
                break;
            case 1:
                targetX = RoutePoint1X;
                targetY = RoutePoint1Y;
                break;
            case 2:
                targetX = RoutePoint2X;
                targetY = RoutePoint2Y;
                break;
        }

        if (targetX == null || targetY == null)
        {
            Log.Error($"CreateDashToPointState: Route Point for point {pointIndex} is null");
            return state;
        }

        // 1. 设置Y速度为0（防止掉落）
        actions.Add(new SetVelocity2d
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            vector = new Vector2(0f, 0f),
            x = new FsmFloat { UseVariable = false },
            y = new FsmFloat(0f),
            everyFrame = false
        });

        // 2. 播放Dash动画（fps已在Dash Antic状态中动态调整）
        actions.Add(new Tk2dPlayAnimationWithEvents
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            clipName = new FsmString("Dash") { Value = "Dash" },
            animationTriggerEvent = null,
            animationCompleteEvent = FsmEvent.Finished // 动画播放完成后触发FINISHED
        });

        // 3. 移动到目标位置（使用速度模式，与动画同步）
        actions.Add(new AnimateXPositionTo
        {
            GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            ToValue = targetX,
            localSpace = false,
            time = new FsmFloat(MAX_DASH_TIME), // 最大时间作为超时保护
            speed = new FsmFloat(DASH_SPEED), // 使用固定速度
            delay = new FsmFloat(0f),
            easeType = EaseFsmAction.EaseType.linear,
            reverse = new FsmBool(false),
            realTime = false
        });

        actions.Add(new AnimateYPositionTo
        {
            GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            ToValue = targetY,
            localSpace = false,
            time = new FsmFloat(MAX_DASH_TIME), // 最大时间作为超时保护
            speed = new FsmFloat(DASH_SPEED), // 使用固定速度
            delay = new FsmFloat(0f),
            easeType = EaseFsmAction.EaseType.linear,
            reverse = new FsmBool(false),
            realTime = false
        });

        state.Actions = actions.ToArray();

        Log.Info($"创建Dash To Point {pointIndex}状态（动态计算时间和动画速度）");
        return state;
    }

    /// <summary>
    /// 创建在指定点位Idle等待的状态（Dash动画完成后进入，播放Idle动画并等待0.6秒）
    /// </summary>
    private FsmState CreateIdleAtPointState(int pointIndex)
    {
        string stateName = pointIndex == -1 ? "Idle At Special" : $"Idle At Point {pointIndex}";
        string description = pointIndex == -1 ? "在Special点位等待" : $"在点位 {pointIndex} 等待";

        var state = new FsmState(_bossControlFsm!.Fsm)
        {
            Name = stateName,
            Description = description
        };

        var actions = new List<FsmStateAction>();


        // 1. 面向英雄（到达点位后面向玩家更合理）
        if (HeroController.instance != null)
        {
            actions.Add(new FaceObjectV2
            {
                objectA = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                objectB = HeroController.instance.gameObject,
                spriteFacesRight = false,
                playNewAnimation = false,
                newAnimationClip = new FsmString("") { Value = "" },
                resetFrame = false,
                pauseBetweenTurns = 0f,
                everyFrame = false
            });
        }

        // 2. 播放Idle动画（衔接Dash动画）
        actions.Add(new Tk2dPlayAnimation
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            animLibName = new FsmString("") { Value = "" },
            clipName = new FsmString("Idle") { Value = "Idle" }
        });

        // 3. 等待可配置的时间后进入下一个点位（使用FsmFloat变量，可后续动态调整）
        actions.Add(new Wait
        {
            time = IdleWaitTime!,
            finishEvent = FsmEvent.Finished,
            realTime = false
        });

        state.Actions = actions.ToArray();

        Log.Info($"创建Idle At Point {pointIndex}状态（面向英雄+Idle动画+等待）");
        return state;
    }

    /// <summary>
    /// 创建Silk Ball Dash End状态（恢复硬直并通知AttackControl）
    /// </summary>
    private FsmState CreateSilkBallDashEndState()
    {
        var state = new FsmState(_bossControlFsm!.Fsm)
        {
            Name = "Silk Ball Dash End",
            Description = "移动丝球结束，恢复硬直"
        };

        var actions = new List<FsmStateAction>();

        // 1. 恢复硬直
        actions.Add(new SendEventByName
        {
            eventTarget = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObject,
                excludeSelf = new FsmBool(false),
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            },
            sendEvent = new FsmString("STUN CONTROL START") { Value = "STUN CONTROL START" },
            delay = new FsmFloat(0f),
            everyFrame = false
        });

        // 2. 通知AttackControl完成
        actions.Add(new SendEventByName
        {
            eventTarget = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObjectFSM,
                excludeSelf = new FsmBool(false),
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
            },
            sendEvent = new FsmString("SILK BALL DASH END") { Value = "SILK BALL DASH END" },
            delay = new FsmFloat(0f),
            everyFrame = false
        });
        // 3. 播放Idle动画
        actions.Add(new Tk2dPlayAnimation
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            animLibName = new FsmString("") { Value = "" },
            clipName = new FsmString("Idle") { Value = "Idle" }
        });

        // 4. 等待一小段时间再返回Idle状态
        actions.Add(new Wait
        {
            time = new FsmFloat(0.5f),
            finishEvent = FsmEvent.Finished,
            realTime = false
        });

        state.Actions = actions.ToArray();

        Log.Info("创建Silk Ball Dash End状态");
        return state;
    }

    #endregion

    #region Helper方法

    /// <summary>
    /// 创建隐形目标点GameObject（位置动态设置）
    /// </summary>
    private void CreateInvisibleTargetPoints()
    {
        // 创建隐形目标点，初始位置设为(0,0,0)
        _targetPointSpecial = new GameObject("DashTargetPointSpecial");
        _targetPointSpecial.transform.position = Vector3.zero;
        _targetPointSpecial.SetActive(true);

        _targetPoint0 = new GameObject("DashTargetPoint0");
        _targetPoint0.transform.position = Vector3.zero;
        _targetPoint0.SetActive(true); // 虽然隐形但需要激活

        _targetPoint1 = new GameObject("DashTargetPoint1");
        _targetPoint1.transform.position = Vector3.zero;
        _targetPoint1.SetActive(true);

        _targetPoint2 = new GameObject("DashTargetPoint2");
        _targetPoint2.transform.position = Vector3.zero;
        _targetPoint2.SetActive(true);

        Log.Info("已创建隐形目标点GameObject（包括Special）");
    }

    /// <summary>
    /// 更新隐形目标点的位置（由AttackControl调用）
    /// </summary>
    public void UpdateTargetPointPositions(Vector3 point0, Vector3 point1, Vector3 point2, Vector3? pointSpecial = null)
    {
        if (pointSpecial.HasValue && _targetPointSpecial != null)
        {
            _targetPointSpecial.transform.position = pointSpecial.Value;
        }

        if (_targetPoint0 != null)
        {
            _targetPoint0.transform.position = point0;
        }

        if (_targetPoint1 != null)
        {
            _targetPoint1.transform.position = point1;
        }

        if (_targetPoint2 != null)
        {
            _targetPoint2.transform.position = point2;
        }

        Log.Info($"已更新目标点位置: P0={point0}, P1={point1}, P2={point2}");
    }

    #endregion

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

        // 1. StartRoarEmitter（复制原版，但 stunHero = false）
        var climbRoarEmitter = new StartRoarEmitter
        {
            Fsm = _bossControlFsm.Fsm,
            spawnPoint = originalEmitter.spawnPoint,
            delay = originalEmitter.delay,
            stunHero = new FsmBool(false) { Value = false },  // 玩家已被硬控，不需要stun
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

