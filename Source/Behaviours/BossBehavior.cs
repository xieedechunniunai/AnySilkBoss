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
namespace AnySilkBoss.Source.Behaviours;

/// <summary>
/// 通用Boss行为控制器基类
/// 这是一个框架，可以用于修改任何Boss的行为
/// 使用示例请参考注释中的机枢舞者实现
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
    private const float MAX_DASH_ANIMATION_TIME = 1.2f; // 最大动画播放时长（秒），避免长距离时动画过慢
    private const float MIN_DASH_ANIMATION_TIME = 0.4f; // 最小动画播放时长（秒），避免短距离时动画过快
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

    // 时间变量
    public FsmFloat? IdleWaitTime;  // Idle等待时间

    // 隐形目标点GameObject（用于FaceObjectV2）
    private GameObject? _targetPoint0;
    private GameObject? _targetPoint1;
    private GameObject? _targetPoint2;

    // FsmGameObject变量（用于存储目标点引用）
    private FsmGameObject? _fsmTargetPoint0;
    private FsmGameObject? _fsmTargetPoint1;
    private FsmGameObject? _fsmTargetPoint2;

    // 原版Dash动画（我们将直接修改其fps）
    private tk2dSpriteAnimationClip? _originalDashClip;
    private float _originalDashFps; // 保存原始fps以便恢复

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
            FsmAnalyzer.WriteFsmReport(_bossControlFsm, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\暂存\\_bossControlFsm.txt");
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
        
        // 为移动丝球状态添加全局中断监听
        AddDashStatesGlobalInterruptHandling();

        // 添加爬升阶段修复
        InitializeClimbCastProtection();
        AddFlipScaleToStaggerPause();
        SetupControlIdlePendingTransitions();
        _bossControlFsm.Fsm.InitData();
        _bossControlFsm.Fsm.InitEvents();
        _bossControlFsm.FsmVariables.Init();
        Log.Info("Boss Control FSM修改完成");
    }

    /// <summary>
    /// 创建移动丝球攻击的状态链
    /// </summary>
    private void CreateSilkBallDashStates()
    {
        if (_bossControlFsm == null) return;

        Log.Info("=== 开始创建移动丝球攻击状态链 ===");

        // 1. 获取并复制原版Dash动画
        GetAndCopyDashAnimations();

        // 2. 创建隐形目标点GameObject
        CreateInvisibleTargetPoints();

        // 3. 初始化Route Point变量（X和Y分开）
        RoutePoint0X = new FsmFloat("Route Point 0 X") { Value = 0f };
        RoutePoint0Y = new FsmFloat("Route Point 0 Y") { Value = 0f };
        RoutePoint1X = new FsmFloat("Route Point 1 X") { Value = 0f };
        RoutePoint1Y = new FsmFloat("Route Point 1 Y") { Value = 0f };
        RoutePoint2X = new FsmFloat("Route Point 2 X") { Value = 0f };
        RoutePoint2Y = new FsmFloat("Route Point 2 Y") { Value = 0f };

        // 4. 初始化时间变量
        IdleWaitTime = new FsmFloat("Idle Wait Time") { Value = IDLE_WAIT_TIME };

        // 5. 初始化FsmGameObject变量（指向隐形目标点）
        _fsmTargetPoint0 = new FsmGameObject("Target Point 0") { Value = _targetPoint0 };
        _fsmTargetPoint1 = new FsmGameObject("Target Point 1") { Value = _targetPoint1 };
        _fsmTargetPoint2 = new FsmGameObject("Target Point 2") { Value = _targetPoint2 };

        // 5. 添加到FSM变量列表
        var floatVars = _bossControlFsm.FsmVariables.FloatVariables.ToList();
        floatVars.Add(RoutePoint0X);
        floatVars.Add(RoutePoint0Y);
        floatVars.Add(RoutePoint1X);
        floatVars.Add(RoutePoint1Y);
        floatVars.Add(RoutePoint2X);
        floatVars.Add(RoutePoint2Y);
        floatVars.Add(IdleWaitTime);
        _bossControlFsm.FsmVariables.FloatVariables = floatVars.ToArray();

        var gameObjectVars = _bossControlFsm.FsmVariables.GameObjectVariables.ToList();
        gameObjectVars.Add(_fsmTargetPoint0);
        gameObjectVars.Add(_fsmTargetPoint1);
        gameObjectVars.Add(_fsmTargetPoint2);
        _bossControlFsm.FsmVariables.GameObjectVariables = gameObjectVars.ToArray();

        Log.Info("Route Point变量（X/Y分离）、时间变量和目标点GameObject变量已创建并添加到BossControl FSM");

        // 创建所有状态
        var dashAntic0State = CreateDashAnticState(0);
        var dashToPoint0State = CreateDashToPointState(0);
        var idleAtPoint0State = CreateIdleAtPointState(0);

        var dashAntic1State = CreateDashAnticState(1);
        var dashToPoint1State = CreateDashToPointState(1);
        var idleAtPoint1State = CreateIdleAtPointState(1);

        var dashAntic2State = CreateDashAnticState(2);
        var dashToPoint2State = CreateDashToPointState(2);
        var dashEndState = CreateSilkBallDashEndState();

        // 添加到FSM
        var states = _bossControlFsm.FsmStates.ToList();
        states.Add(dashAntic0State);
        states.Add(dashToPoint0State);
        states.Add(idleAtPoint0State);
        states.Add(dashAntic1State);
        states.Add(dashToPoint1State);
        states.Add(idleAtPoint1State);
        states.Add(dashAntic2State);
        states.Add(dashToPoint2State);
        states.Add(dashEndState);
        _bossControlFsm.Fsm.States = states.ToArray();

        // 设置转换
        SetupSilkBallDashTransitions(
            dashAntic0State, dashToPoint0State, idleAtPoint0State,
            dashAntic1State, dashToPoint1State, idleAtPoint1State,
            dashAntic2State, dashToPoint2State,
            dashEndState);

        // InjectStunHandlingIntoBossControl();

        // 初始化FSM
        _bossControlFsm.Fsm.InitData();
        _bossControlFsm.Fsm.InitEvents();

        Log.Info("移动丝球状态链创建完成");
    }

    /// <summary>
    /// 设置移动丝球状态链的转换关系
    /// </summary>
    private void SetupSilkBallDashTransitions(
        FsmState dashAntic0, FsmState dashToPoint0, FsmState idleAtPoint0,
        FsmState dashAntic1, FsmState dashToPoint1, FsmState idleAtPoint1,
        FsmState dashAntic2, FsmState dashToPoint2,
        FsmState dashEnd)
    {
        // Point 0
        dashAntic0.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = dashToPoint0.Name, toFsmState = dashToPoint0 } };
        dashToPoint0.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = idleAtPoint0.Name, toFsmState = idleAtPoint0 } };
        idleAtPoint0.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = dashAntic1.Name, toFsmState = dashAntic1 } };

        // Point 1
        dashAntic1.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = dashToPoint1.Name, toFsmState = dashToPoint1 } };
        dashToPoint1.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = idleAtPoint1.Name, toFsmState = idleAtPoint1 } };
        idleAtPoint1.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = dashAntic2.Name, toFsmState = dashAntic2 } };

        // Point 2
        dashAntic2.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = dashToPoint2.Name, toFsmState = dashToPoint2 } };
        dashToPoint2.Transitions = new FsmTransition[] { new FsmTransition { FsmEvent = FsmEvent.Finished, toState = dashEnd.Name, toFsmState = dashEnd } };

        // Dash End -> Idle
        var idleState = _bossControlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Idle");
        if (idleState != null)
        {
            dashEnd.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Idle",
                    toFsmState = idleState
                }
            };
        }
    }

    /// <summary>
    /// 创建Dash准备状态
    /// </summary>
    private FsmState CreateDashAnticState(int pointIndex)
    {
        var state = new FsmState(_bossControlFsm!.Fsm)
        {
            Name = $"Dash Antic {pointIndex}",
            Description = $"准备冲刺到点位 {pointIndex}"
        };

        var actions = new List<FsmStateAction>();

        // 获取目标GameObject和坐标
        FsmGameObject? targetObject = null;

        switch (pointIndex)
        {
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

        // 1. 计算距离和调整动画速度（使用 CallMethod 调用我们的方法）
        // 注意：每次Antic都会根据当前实际位置和目标位置动态计算距离，并调整Dash动画的fps
        // 这样可以确保每段不同距离的冲刺都有匹配的动画时长
        actions.Add(new CallMethod
        {
            behaviour = this,
            methodName = nameof(CalculateAndAdjustDashAnimationByIndex),
            parameters = new FsmVar[]
            {
                new FsmVar(typeof(int)) { intValue = pointIndex }
            },
            everyFrame = false
        });

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
    /// 计算冲刺距离并调整原版Dash动画的fps（由CallMethod调用）
    /// </summary>
    public void CalculateAndAdjustDashAnimationByIndex(int pointIndex)
    {
        // 获取对应的目标坐标
        FsmFloat? targetX = null;
        FsmFloat? targetY = null;

        switch (pointIndex)
        {
            case 0:
                targetX = RoutePoint0X;
                targetY = RoutePoint0Y;
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
            Log.Error($"CalculateAndAdjustDashAnimationByIndex: Target coordinates for point {pointIndex} are null");
            return;
        }

        if (_originalDashClip == null)
        {
            Log.Error("CalculateAndAdjustDashAnimationByIndex: Original Dash clip is null");
            return;
        }

        Vector2 currentPos = transform.position;
        Vector2 targetPos = new Vector2(targetX.Value, targetY.Value);

        float distance = Vector2.Distance(currentPos, targetPos);
        float dashTime = Mathf.Max(distance / DASH_SPEED, 0.1f);

        // 调整动画时长：限制在合理范围内
        // 长距离时：动画快速循环播放，保持节奏感
        // 短距离时：动画正常播放，不会过快
        // float adjustedDashTime = Mathf.Clamp(dashTime, MIN_DASH_ANIMATION_TIME, MAX_DASH_ANIMATION_TIME);

        // 直接调整原版Dash动画的fps以匹配调整后的动画时长（使用整数fps）
        // if (_originalDashClip.frames != null && _originalDashClip.frames.Length > 0)
        // {
        //     float calculatedFps = _originalDashClip.frames.Length * 2f / dashTime;
        //     _originalDashClip.fps = Mathf.RoundToInt(calculatedFps);
        //     Log.Info($"Point {pointIndex} 动态调整Dash动画: Distance={distance:F2}, DashTime={dashTime:F2}s, ClampedAnimTime={dashTime:F2}s (Clamp:{MIN_DASH_ANIMATION_TIME}-{MAX_DASH_ANIMATION_TIME}), CalculatedFPS={calculatedFps:F2}, FinalFPS={_originalDashClip.fps}");
        // }
    }

    /// <summary>
    /// 恢复原版Dash动画的fps（在Idle状态或攻击结束后调用）
    /// </summary>
    public void RestoreOriginalDashFps()
    {
        if (_originalDashClip != null)
        {
            _originalDashClip.fps = _originalDashFps;
            Log.Info($"恢复原版Dash动画fps: {_originalDashFps}");
        }
    }

    /// <summary>
    /// 获取原版Dash动画并复制多份用于不同点位的移动
    /// </summary>
    private void GetAndCopyDashAnimations()
    {
        if (_bossControlFsm == null)
        {
            Log.Error("BossControl FSM为null，无法获取Dash动画");
            return;
        }

        // 1. 获取原版Dash状态
        var originalDashState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Dash");
        if (originalDashState == null)
        {
            Log.Error("未找到原版Dash状态");
            return;
        }

        // 2. 从Dash状态的Actions中获取Tk2dPlayAnimationWithEvents
        var playAnimAction = originalDashState.Actions.OfType<Tk2dPlayAnimationWithEvents>().FirstOrDefault();
        if (playAnimAction == null)
        {
            Log.Error("Dash状态中未找到Tk2dPlayAnimationWithEvents动作");
            return;
        }

        // 3. 获取原版动画clip（从tk2dSpriteAnimator组件获取）
        var animator = GetComponent<tk2dSpriteAnimator>();
        if (animator == null)
        {
            Log.Error("未找到tk2dSpriteAnimator组件");
            return;
        }

        _originalDashClip = animator.GetClipByName("Dash");
        if (_originalDashClip == null)
        {
            Log.Error("未找到名为'Dash'的动画clip");
            return;
        }

        // 保存原始fps
        _originalDashFps = _originalDashClip.fps;

        Log.Info($"获取到原版Dash动画: fps={_originalDashClip.fps}, frames={_originalDashClip.frames?.Length}, duration={(_originalDashClip.frames?.Length ?? 0) / _originalDashClip.fps}秒");
        Log.Info("将在每次Dash前动态调整原版Dash动画的fps以匹配移动时间");
    }


    /// <summary>
    /// 创建大丝球大招锁定状态
    /// </summary>
    private void CreateBigSilkBallLockState()
    {
        if (_bossControlFsm == null) return;

        Log.Info("创建大丝球大招锁定状态");

        // 创建锁定状态
        var lockState = new FsmState(_bossControlFsm.Fsm)
        {
            Name = "Big Silk Ball Lock",
            Description = "大丝球大招期间锁定BOSS，只播放Idle动画"
        };

        // 添加动作：播放漂浮动画
        var actions = new List<FsmStateAction>();
        

        lockState.Actions = actions.ToArray();

        // 添加状态到FSM
        var states = _bossControlFsm.Fsm.States.ToList();
        states.Add(lockState);
        _bossControlFsm.Fsm.States = states.ToArray();

        // 添加转换：只监听BIG SILK BALL UNLOCK事件
        var idleState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
        if (idleState != null)
        {
            lockState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("BIG SILK BALL UNLOCK"),
                    toState = "Idle",
                    toFsmState = idleState
                }
            };
        }

        // 重新初始化FSM
        _bossControlFsm.Fsm.InitData();

        Log.Info("大丝球大招锁定状态创建完成");
    }

    /// <summary>
    /// 添加全局事件监听（在任意状态都可以响应）
    /// </summary>
    private void AddGlobalEventListeners()
    {
        if (_bossControlFsm == null) return;

        var idleState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
        if (idleState == null)
        {
            Log.Error("未找到Idle状态，跳过添加FORCE IDLE全局监听");
            return;
        }

        var lockState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Big Silk Ball Lock");
        if (lockState == null)
        {
            Log.Warn("未找到Big Silk Ball Lock状态，跳过添加BIG SILK BALL LOCK全局监听");
        }

        // 使用反射添加到FSM的全局转换列表
        var globalTransitions = _bossControlFsm.FsmGlobalTransitions.ToList();
        
        // 添加FORCE IDLE全局转换，用于强制Boss回到Idle状态
        globalTransitions.Add(new FsmTransition
        {
            FsmEvent = FsmEvent.GetFsmEvent("FORCE IDLE"),
            toState = "Idle",
            toFsmState = idleState
        });

        // 添加BIG SILK BALL LOCK全局转换，用于大招锁定Boss
        if (lockState != null)
        {
            globalTransitions.Add(new FsmTransition
            {
                FsmEvent = FsmEvent.GetFsmEvent("BIG SILK BALL LOCK"),
                toState = "Big Silk Ball Lock",
                toFsmState = lockState
            });
        }
        _bossControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
        // 使用反射设置FsmGlobalTransitions（因为它是只读属性）
        // var fsmType = _bossControlFsm.Fsm.GetType();
        // var globalTransitionsField = fsmType.GetField("globalTransitions",
        //     System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // if (globalTransitionsField != null)
        // {
        //     globalTransitionsField.SetValue(_bossControlFsm.Fsm, globalTransitions.ToArray());
        //     Log.Info("已通过反射添加FORCE IDLE与BIG SILK BALL LOCK到全局转换（FsmGlobalTransitions）");
        // }
        // else
        // {
        //     Log.Error("未找到globalTransitions字段，无法设置全局转换");
        // }
    }

    /// <summary>
    /// 创建Dash到指定点位的状态（使用FaceObjectV2面向目标，Tk2dPlayAnimationWithEvents播放Dash动画）
    /// </summary>
    private FsmState CreateDashToPointState(int pointIndex)
    {
        var state = new FsmState(_bossControlFsm!.Fsm)
        {
            Name = $"Dash To Point {pointIndex}",
            Description = $"冲刺到点位 {pointIndex}"
        };

        var actions = new List<FsmStateAction>();

        // 获取目标坐标
        FsmFloat? targetX = null;
        FsmFloat? targetY = null;

        switch (pointIndex)
        {
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
        var state = new FsmState(_bossControlFsm!.Fsm)
        {
            Name = $"Idle At Point {pointIndex}",
            Description = $"在点位 {pointIndex} 等待"
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

        // 0. 恢复原版Dash动画的fps（在攻击结束时）
        actions.Add(new CallMethod
        {
            behaviour = this,
            methodName = nameof(RestoreOriginalDashFps),
            parameters = new FsmVar[0],
            everyFrame = false
        });

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
    /// 创建3个隐形目标点GameObject（位置动态设置）
    /// </summary>
    private void CreateInvisibleTargetPoints()
    {
        // 创建3个隐形目标点，初始位置设为(0,0,0)
        _targetPoint0 = new GameObject("DashTargetPoint0");
        _targetPoint0.transform.position = Vector3.zero;
        _targetPoint0.SetActive(true); // 虽然隐形但需要激活

        _targetPoint1 = new GameObject("DashTargetPoint1");
        _targetPoint1.transform.position = Vector3.zero;
        _targetPoint1.SetActive(true);

        _targetPoint2 = new GameObject("DashTargetPoint2");
        _targetPoint2.transform.position = Vector3.zero;
        _targetPoint2.SetActive(true);

        Log.Info("已创建3个隐形目标点GameObject");
    }

    /// <summary>
    /// 更新隐形目标点的位置（由AttackControl调用）
    /// </summary>
    public void UpdateTargetPointPositions(Vector3 point0, Vector3 point1, Vector3 point2)
    {
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

    /// <summary>
    /// 修改原版FSM，在Idle状态添加恢复Dash动画fps的逻辑
    /// </summary>
    // public void ModifyOriginFsm()
    // {
    //     // 0. 恢复原版Dash动画的fps（在Idle状态开头）
    //     var originalIdleState = _bossControlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Idle");
    //     if (originalIdleState == null)
    //     {
    //         Log.Error("未找到原版Idle状态");
    //         return;
    //     }
    //     var actions = originalIdleState.Actions.ToList();
    //     actions.Insert(0, new CallMethod
    //     {
    //         behaviour = this,
    //         methodName = nameof(RestoreOriginalDashFps),
    //         parameters = new FsmVar[0],
    //         everyFrame = false
    //     });
    //     originalIdleState.Actions = actions.ToArray();
    // }

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

        // 在状态开头添加发送中断事件给AttackControl
        actions.Insert(1, new SendEventByName
        {
            eventTarget = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObjectFSM,
                excludeSelf = new FsmBool(false),
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
            },
            sendEvent = new FsmString("SILK BALL INTERRUPT") { Value = "SILK BALL INTERRUPT" },
            delay = new FsmFloat(0f),
            everyFrame = false
        });

        // 添加额外的状态同步方法调用（防止FSM状态不同步）
        // actions.Insert(2, new CallMethod
        // {
        //     behaviour = this,
        //     methodName = new FsmString("SyncAttackControlStateOnStun") { Value = "SyncAttackControlStateOnStun" },
        //     parameters = new FsmVar[0],
        //     everyFrame = false
        // });

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
            var silkBallManager = managerObj.GetComponent<AnySilkBoss.Source.Managers.SilkBallManager>();
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

    /// <summary>
    /// 为移动丝球的所有Dash状态添加全局中断监听
    /// 确保眩晕时能正确中断移动状态
    /// </summary>
    private void AddDashStatesGlobalInterruptHandling()
    {
        if (_bossControlFsm == null) return;

        Log.Info("=== 为移动丝球状态添加全局中断监听 ===");

        // 找到Idle状态作为中断目标
        var idleState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
        if (idleState == null)
        {
            Log.Error("未找到Idle状态，无法添加中断监听");
            return;
        }

        // 获取所有需要添加中断的状态
        var dashStates = new[]
        {
            "Dash Antic 0", "Dash To Point 0", "Idle At Point 0",
            "Dash Antic 1", "Dash To Point 1", "Idle At Point 1",
            "Dash Antic 2", "Dash To Point 2"
        };

        var interruptEvent = FsmEvent.GetFsmEvent("SILK BALL INTERRUPT");
        
        foreach (var stateName in dashStates)
        {
            var state = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == stateName);
            if (state == null)
            {
                Log.Warn($"未找到状态: {stateName}");
                continue;
            }

            // 添加中断转换到现有转换列表
            var transitions = state.Transitions.ToList();
            
            // 检查是否已存在该事件的转换
            if (!transitions.Any(t => t.FsmEvent == interruptEvent))
            {
                transitions.Add(new FsmTransition
                {
                    FsmEvent = interruptEvent,
                    toState = "Idle",
                    toFsmState = idleState
                });
                
                state.Transitions = transitions.ToArray();
                Log.Info($"已为状态 {stateName} 添加中断转换");
            }
        }

        // 重新初始化FSM
        _bossControlFsm.Fsm.InitData();
        _bossControlFsm.Fsm.InitEvents();

        Log.Info("移动丝球状态全局中断监听添加完成");
    }

    #endregion

    #region 爬升阶段漫游系统
    
    // 漫游相关变量
    private Vector3 _currentRoamTarget;
    private float _roamMoveStartTime;
    private bool _roamMoveComplete = false;

    /// <summary>
    /// 创建爬升阶段漫游状态链
    /// </summary>
    private void CreateClimbRoamStates()
    {
        if (_bossControlFsm == null) return;

        Log.Info("=== 开始创建爬升阶段漫游状态链 ===");

        // 创建四个漫游状态
        var climbRoamInit = CreateClimbRoamInitState();
        var climbRoamSelectTarget = CreateClimbRoamSelectTargetState();
        var climbRoamMove = CreateClimbRoamMoveState();
        var climbRoamIdle = CreateClimbRoamIdleState();

        // 找到Idle状态用于转换
        var idleState = _bossControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
        
        // 添加状态到FSM
        var states = _bossControlFsm.FsmStates.ToList();
        states.Add(climbRoamInit);
        states.Add(climbRoamSelectTarget);
        states.Add(climbRoamMove);
        states.Add(climbRoamIdle);
        _bossControlFsm.Fsm.States = states.ToArray();

        // 添加动作
        AddClimbRoamInitActions(climbRoamInit);
        AddClimbRoamSelectTargetActions(climbRoamSelectTarget);
        AddClimbRoamMoveActions(climbRoamMove);
        AddClimbRoamIdleActions(climbRoamIdle);

        // 添加转换
        AddClimbRoamTransitions(climbRoamInit, climbRoamSelectTarget, 
            climbRoamMove, climbRoamIdle);

        // 添加全局转换
        AddClimbPhaseGlobalTransitions(climbRoamInit, idleState);

        // 初始化FSM
        _bossControlFsm.Fsm.InitData();
        _bossControlFsm.Fsm.InitEvents();

        Log.Info("=== 爬升阶段漫游状态链创建完成 ===");
    }

    private FsmState CreateClimbRoamInitState()
    {
        return new FsmState(_bossControlFsm!.Fsm)
        {
            Name = "Climb Roam Init",
            Description = "漫游初始化"
        };
    }

    private FsmState CreateClimbRoamSelectTargetState()
    {
        return new FsmState(_bossControlFsm!.Fsm)
        {
            Name = "Climb Roam Select Target",
            Description = "选择漫游目标"
        };
    }

    private FsmState CreateClimbRoamMoveState()
    {
        return new FsmState(_bossControlFsm!.Fsm)
        {
            Name = "Climb Roam Move",
            Description = "移动到目标"
        };
    }

    private FsmState CreateClimbRoamIdleState()
    {
        return new FsmState(_bossControlFsm!.Fsm)
        {
            Name = "Climb Roam Idle",
            Description = "短暂停留"
        };
    }

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
        // Init -> Select Target
        initState.Transitions = new FsmTransition[]
        {
            new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Climb Roam Select Target",
                toFsmState = selectState
            }
        };

        // Select Target -> Move
        selectState.Transitions = new FsmTransition[]
        {
            new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Climb Roam Move",
                toFsmState = moveState
            }
        };

        // Move -> Idle
        moveState.Transitions = new FsmTransition[]
        {
            new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Climb Roam Idle",
                toFsmState = idleState
            }
        };

        // Idle -> Select Target (循环)
        idleState.Transitions = new FsmTransition[]
        {
            new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Climb Roam Select Target",
                toFsmState = selectState
            }
        };

        Log.Info("漫游状态转换设置完成");
    }

    private void AddClimbPhaseGlobalTransitions(FsmState climbRoamInit, FsmState? idleState)
    {
        var globalTransitions = _bossControlFsm!.Fsm.GlobalTransitions.ToList();
        
        // 收到 CLIMB PHASE START → Climb Roam Init
        globalTransitions.Add(new FsmTransition
        {
            FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE START"),
            toState = "Climb Roam Init",
            toFsmState = climbRoamInit
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
        float moveSpeed = 6f;  // 
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
        controlFsm.Fsm.InitEvents();
        
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
        if (controlFsm.FsmStates.Any(s => s.Name == "Climb Cast Prepare"))
        {
            Log.Info("Climb Cast Prepare状态已存在，跳过创建");
            return;
        }
        
        // 创建新状态
        var climbCastPrepareState = new FsmState(controlFsm.Fsm)
        {
            Name = "Climb Cast Prepare",
            Description = "爬升Cast动画保护状态（长Wait时间）"
        };
        
        var actions = new List<FsmStateAction>();

        var climbCastPendingVar = EnsureBoolVariable(controlFsm, "Climb Cast Pending");
        actions.Add(new SetBoolValue
        {
            boolVariable = climbCastPendingVar,
            boolValue = new FsmBool(false)
        });
        
        // 添加Wait动作（时间更长，用于保护Cast动画）
        actions.Add(new Wait
        {
            time = new FsmFloat(2.5f), // 比原版的0.8秒更长
            finishEvent = FsmEvent.Finished,
            realTime = false
        });
        
        climbCastPrepareState.Actions = actions.ToArray();
        
        // 添加转换：FINISHED → Idle
        var idleState = controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
        if (idleState != null)
        {
            climbCastPrepareState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Idle",
                    toFsmState = idleState
                }
            };
        }
        
        // 添加到FSM
        var states = controlFsm.FsmStates.ToList();
        states.Add(climbCastPrepareState);
        controlFsm.Fsm.States = states.ToArray();
        
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

        var climbPendingVar = EnsureBoolVariable(_bossControlFsm, "Climb Cast Pending");
        var dashPendingVar = EnsureBoolVariable(_bossControlFsm, "Silk Ball Dash Pending");

        var actions = idleState.Actions?.ToList() ?? new List<FsmStateAction>();
        actions.RemoveAll(action => action is BoolTest boolTest &&
            (boolTest.boolVariable == climbPendingVar || boolTest.boolVariable == dashPendingVar));

        var climbBridgeEvent = FsmEvent.GetFsmEvent("CLIMB CAST BRIDGE");
        var dashBridgeEvent = FsmEvent.GetFsmEvent("SILK BALL DASH BRIDGE");

        if (dashAntic0State != null)
        {
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
        transitions.RemoveAll(t => t.FsmEvent == climbBridgeEvent || t.FsmEvent == dashBridgeEvent);

        if (climbCastState != null)
        {
            transitions.Add(new FsmTransition
            {
                FsmEvent = climbBridgeEvent,
                toState = climbCastState.Name,
                toFsmState = climbCastState
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

    /// <summary>
    /// 在Stagger Pause状态中添加翻转回来的动作
    /// </summary>
    private void AddFlipScaleToStaggerPause()
    {
        var staggerPauseState = _bossControlFsm!.FsmStates
            .FirstOrDefault(s => s.Name == "Stagger Pause");
        
        if (staggerPauseState == null)
        {
            Log.Warn("未找到Stagger Pause状态");
            return;
        }
        
        // 检查是否已存在FlipScale
        if (staggerPauseState.Actions.OfType<FlipScale>().Any())
        {
            Log.Info("Stagger Pause中已存在FlipScale，跳过添加");
            return;
        }
        
        var actions = staggerPauseState.Actions.ToList();
        
        // 在状态开头添加FlipScale（翻转回来）
        actions.Insert(0, new FlipScale
        {
            gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
            flipHorizontally = true,
            flipVertically = false,
            everyFrame = false
        });
        
        staggerPauseState.Actions = actions.ToArray();
        
        Log.Info("在Stagger Pause中添加FlipScale完成");
    }

    #endregion
}

