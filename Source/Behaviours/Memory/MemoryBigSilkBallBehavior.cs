using System.Collections;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Behaviours.Common;
using System.Linq;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory;

/// <summary>
/// 大丝球Behavior - 管理单个大丝球的行为和FSM
/// 用于天女散花大招
/// 
/// === 时间规划 ===
/// 阶段1：吸收蓄力（Absorb Charge）
///   - 保底时长：10秒
///   - 每秒生成10个小丝球追踪大丝球
///   - 吸收30个小丝球后达到最大尺寸(0.7)，停止生成但继续吸收已生成的
///   
/// 阶段2：射击波次（Shoot Waves）- 动画播放开始
///   - 7波定向抛射
///   - 每波3个小丝球，间隔1秒
///   - 总时长：约9秒
///   
/// 阶段3：最终爆炸（Final Burst）- 超出动画时间
///   - 4圈同心圆，每圈20个小丝球
///   - 圈生成间隔0.5秒
///   - 所有圈生成完后延迟1.0秒统一爆发
///   - 总时长：约3秒
///   
/// 动画覆盖：Silk_Cocoon_Intro_Burst (11.53秒) 覆盖阶段2大部分和阶段3开始
/// 总时长：吸收10s + 射击9s + 爆炸3s = 约22秒
/// </summary>
internal class MemoryBigSilkBallBehavior : MonoBehaviour
{
    #region 参数配置
    [Header("Boss引用")]
    public GameObject? bossObject;
    private MemoryPhaseControlBehavior? phaseControlBehavior;  // PhaseControl引用

    [Header("位置参数")]
    public Vector3 chestOffset = new Vector3(0f, -1f, 0f);  // 胸前偏移量

    [Header("碰撞箱参数")]
    public float collisionBoxRadius = 5f;    // 碰撞箱半径
    public float initialScale = 0.1f;        // 初始缩放
    public float maxScale = 0.7f;            // 最大缩放

    [Header("爆炸参数")]
    public string burstAnimationName = "Silk_Cocoon_Intro_Burst";  // 爆炸动画名称
    public float burstDuration = 11.53f;     // 爆炸持续时间（根据动画实际时长）
    public int ballsPerWave = 3;
    [Header("吸收蓄力参数")]
    public float absorbDuration = 10f;       // 保底持续时间
    public float absorbSpawnRate = 10f;      // 每秒生成数量
    public float absorbSpawnRadius = 40f;    // 生成半径
    public int absorbCountToMax = 30;        // 达到最大需要吸收的数量
    public float scaleIncreasePerAbsorb = 0.012f; // 每次吸收增量
    public float lowerHalfProbability = 0.7f;    // 下半部分生成概率
    public float absorbAudioVolumeMultiplier = 3f;
    [Header("抛射波次参数")]
    public float shootSpeed = 13f;           // 抛射基础速度
    public float shootSpeedRandomRange = 5f; // 速度随机范围（±单位/秒）
    public float shootAngleRandomRange = 15f; // 角度随机范围（±度）
    public float shootGravityScale = 0.0005f;   // 抛射重力缩放（轻微下坠）
    public float shootAcceleration = 15f;     // 抛射追踪加速度（转向能力，正常追踪为30）

    // 7波抛射配置：(生成角度, 生成半径倍数, 抛射角度, 球数量, 波间隔)
    // 半径基于碰撞箱radius=5
    private readonly (float spawnAngle, float radiusMult, float shootAngle, int ballCount, float interval)[] _shootWaveConfigs = new[]
    {
            (352.5f, 0.5f, 65f, 7, 0.7f),    // 第1波：3.15位置，半径一半，更朝上
            (0f, 0f, 60f, 6, 0.35f),         // 第2波：中心，更朝上
            (45f, 0.125f, 65f, 6, 0.35f),    // 第3波：1.30方向，半径1/8，更朝上
            (130f, 0.5f, 105f, 10, 0.5f),    // 第4波：10.40位置，半径一半，更朝上
            (0f, 0f, 80f, 6, 0.35f),         // 第5波：中心，更朝上
            (0f, 0f, 120f, 10, 1.0f),        // 第6波：中心，更朝上
            (0f, 0f, 90f, 10, 0.5f)          // 第7波：中心，向正上方
        };

    [Header("最终爆炸参数")]
    public int finalBurstRings = 5;          // 圈数（从4提升到5）
    public int ballsPerRing = 20;            // 每圈数量
    public float[] ringRadii = new float[] { 1f, 2f, 3f, 4f, 5f };  // 各圈半径（添加第5圈）
    public float orbitalAngularSpeed = 60f;  // 公转角速度（度/秒）
    public float ringSpawnInterval = 0.5f;   // 圈间隔爆发
    public float ringBurstDelay = 1.6f;      // 生成完后延迟爆发
    public float burstSpeed = 18f;           // 爆发速度
    public float innerRingSpeedMultiplier = 1.2f;  // 内圈速度倍数
    public float outerRingSpeedMultiplier = 0.8f;  // 外圈速度倍数
    public float finalBurstGravityScale = 0f;     // 最终爆炸重力缩放（0表示无重力）
    #endregion

    #region 组件引用
    private PlayMakerFSM? controlFSM;
    private Animator? animator;
    private Managers.SilkBallManager? silkBallManager;

    [Header("内部引用")]
    public Transform? heartTransform;  // heart子物品的Transform，用于跟随BOSS（需要public供FSM Action访问）
    public GameObject? collisionBox;   // 碰撞箱GameObject（Z=0位置，实际碰撞）
    public Transform? collisionBoxTransform;  // 碰撞箱Transform
    private MemoryBigSilkBallCollisionBox? collisionBoxScript;  // 碰撞箱脚本

    // 英雄引用（用于碰撞箱位置跟随）
    private GameObject? heroObject;
    private float lastHeroX = 0f;
    private Vector3 collisionBoxBaseLocalPos;  // 碰撞箱的基础本地位置

    // 吸收音效资源（从原版预制体提取）
    private AudioClip? absorbAudioClip;
    private float absorbAudioPitchMin = 1f;
    private float absorbAudioPitchMax = 1f;
    private float absorbAudioVolume = 1f;  // 缓存的吸收音效 action（从小丝球 Disappear 状态提取）
    #endregion

    #region FSM 变量引用
    private FsmGameObject? bossTransformVar;
    private FsmVector3? chestOffsetVar;
    private FsmFloat? maxScaleVar;
    #endregion

    #region 事件引用
    private FsmEvent? startChargeEvent;
    private FsmEvent? animationCompleteEvent;
    #endregion

    #region 状态标记
    private bool isInitialized = false;
    private bool isAbsorbing = false;         // 是否正在吸收
    private int absorbedCount = 0;            // 已吸收数量
    private float currentScale = 0.1f;        // 当前缩放
    private bool shouldStopSpawning = false;  // 是否停止生成
    private bool shouldUpdateCollisionBoxPosition = true;  // 是否更新碰撞箱位置（Final Burst阶段禁用）

    // 强制覆盖Animator控制的标志
    // 只覆盖heart的缩放（蓄力动画），位置（包括Z轴）保持原版
    private bool forceOverrideScale = false;            // 是否强制覆盖缩放
    private Vector3 targetHeartScale;                   // 目标缩放（本地缩放）

    // 最终爆炸阶段保存的圆心位置（碰撞箱位置快照）
    private Vector3 savedBurstCenter;                   // 爆发圆心（大丝球销毁后小丝球仍可使用）
    #endregion

    /// <summary>
    /// 初始化大丝球（从管理器调用）
    /// </summary>
    /// <param name="rootObject">根物品GameObject（用于获取Animator等组件）</param>
    /// <param name="boss">Boss对象</param>
    /// <param name="heart">heart子物品Transform（用于位置和缩放操作）</param>
    public void Initialize(GameObject rootObject, GameObject boss, Transform? heart = null)
    {
        if (isInitialized)
        {
            Log.Warn("大丝球已初始化");
            return;
        }

        bossObject = boss;
        heartTransform = heart;  // 保存heart引用

        // heart和所有子物品保持原版相对位置（包括Z轴），只通过移动根物品的XY轴跟随BOSS
        if (heartTransform != null)
        {
            Log.Info($"heart保持原版相对位置: {heartTransform.localPosition}");
            Log.Info($"将只调整根物品XY轴跟随BOSS，Z轴保持原版");
        }

        // 获取PhaseControlBehavior引用
        if (boss != null)
        {
            phaseControlBehavior = boss.GetComponent<MemoryPhaseControlBehavior>();
            if (phaseControlBehavior == null)
            {
                Log.Warn("未找到 PhaseControlBehavior 组件");
            }
        }

        // 获取英雄引用（用于碰撞箱位置跟随）
        heroObject = HeroController.instance?.gameObject;
        if (heroObject != null)
        {
            lastHeroX = heroObject.transform.position.x;
            Log.Info($"成功获取英雄引用，初始X位置: {lastHeroX}");
        }
        SilkSpool silkSpool = SilkSpool.Instance;
        // 从根物品获取组件（Animator等）
        GetComponentReferences(rootObject);

        // 创建碰撞箱
        CreateCollisionBox();

        // 创建 FSM
        CreateControlFSM();

        isInitialized = true;
        Log.Info($"大丝球初始化完成，heart引用: {(heartTransform != null ? "已设置" : "未设置")}，碰撞箱: {(collisionBox != null ? "已创建" : "未创建")}");
    }

    private void Update()
    {
        // 更新碰撞箱X轴位置，跟随英雄移动
        UpdateCollisionBoxPosition();

        // 调试快捷键
        if (Input.GetKeyDown(KeyCode.T))
        {
            LogBigSilkBallFSMInfo();
        }
    }

    /// <summary>
    /// 更新碰撞箱X轴位置，跟随英雄移动
    /// 由于大丝球在Z=57的背景，视角移动会导致显示位置和碰撞箱位置偏差
    /// </summary>
    private void UpdateCollisionBoxPosition()
    {
        // Final Burst阶段禁用位置更新，避免影响小丝球受力计算
        if (!shouldUpdateCollisionBoxPosition) return;
        
        if (collisionBox == null || heroObject == null) return;

        float currentHeroX = heroObject.transform.position.x;

        // 定义英雄X轴范围和对应的碰撞箱本地X轴相对偏移
        // 英雄X轴从 29 到 46 之间，碰撞箱本地X轴从 -2.5 到 6.8 相对偏移
        float heroMinX = 29f;
        float heroMaxX = 46f;
        float collisionBoxRelativeMinX = -2.5f;
        float collisionBoxRelativeMaxX = 6.8f;

        // 将当前英雄X轴映射到碰撞箱相对X轴的范围
        // 使用 Mathf.InverseLerp 将值归一化到 0-1 范围，然后用 Mathf.Lerp 映射到目标范围
        float t = Mathf.InverseLerp(heroMinX, heroMaxX, currentHeroX);
        float targetRelativeX = Mathf.Lerp(collisionBoxRelativeMinX, collisionBoxRelativeMaxX, t);

        Vector3 currentLocalPos = collisionBox.transform.localPosition;
        currentLocalPos.x = collisionBoxBaseLocalPos.x + targetRelativeX; // 加上基础本地X和计算出的相对偏移
        collisionBox.transform.localPosition = currentLocalPos;

    }

    /// <summary>
    /// LateUpdate在Animator更新之后执行，用于强制覆盖Animator对heart的控制
    /// 只覆盖缩放，位置（包括Z轴）保持原版，跟随根物品移动
    /// </summary>
    private void LateUpdate()
    {
        if (heartTransform == null) return;

        // 强制覆盖缩放（蓄力动画），防止Animator重置heart的大小
        if (forceOverrideScale)
        {
            heartTransform.localScale = targetHeartScale;
        }
    }

    private void LogBigSilkBallFSMInfo()
    {
        if (controlFSM != null)
        {
            Log.Info($"=== 大丝球 Control FSM 信息 ===");
            Log.Info($"FSM名称: {controlFSM.FsmName}");
            Log.Info($"当前状态: {controlFSM.ActiveStateName}");
            FsmAnalyzer.WriteFsmReport(controlFSM, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\_bigSilkBallControlFsm.txt");
        }
    }

    /// <summary>
    /// 获取组件引用
    /// </summary>
    /// <param name="rootObject">根物品GameObject（从根物品获取Animator等组件）</param>
    private void GetComponentReferences(GameObject rootObject)
    {
        // 从根物品获取 Animator
        animator = rootObject.GetComponent<Animator>();
        if (animator == null)
        {
            Log.Warn("未找到 Animator 组件");
        }
        else
        {
            Log.Info($"找到 Animator 组件，RuntimeAnimatorController: {animator.runtimeAnimatorController?.name ?? "null"}");

            // 验证是否有可用的动画片段
            if (animator.runtimeAnimatorController != null)
            {
                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips != null && clips.Length > 0)
                {
                    Log.Info($"Animator 包含 {clips.Length} 个动画片段");
                    foreach (var clip in clips)
                    {
                        Log.Info($"  - {clip.name} ({clip.length:F2}s)");
                    }
                }
                else
                {
                    Log.Warn("Animator 没有动画片段");
                }
            }
        }

        // 获取 SilkBallManager
        var managerObj = GameObject.Find("AnySilkBossManager");
        if (managerObj != null)
        {
            silkBallManager = managerObj.GetComponent<Managers.SilkBallManager>();
            if (silkBallManager == null)
            {
                Log.Warn("未找到 SilkBallManager 组件");
            }
            else
            {
                // 提取吸收音效
                ExtractAbsorbAudioAction();
            }
        }
        else
        {
            Log.Warn("未找到 AnySilkBossManager 对象");
        }
    }

    /// <summary>
    /// 创建碰撞箱GameObject（Z=0位置）
    /// </summary>
    private void CreateCollisionBox()
    {
        // 在根物品下创建碰撞箱GameObject
        collisionBox = new GameObject("CollisionBox");
        collisionBox.transform.parent = transform;

        // 设置位置：XY跟heart一致(-9.4, -2.9)，Z轴设为0（世界坐标）
        collisionBoxBaseLocalPos = new Vector3(-6.4f, -5f, -57.4491f);  // 保存基础本地位置
        collisionBox.transform.localPosition = collisionBoxBaseLocalPos;

        // 添加碰撞箱脚本
        collisionBoxScript = collisionBox.AddComponent<MemoryBigSilkBallCollisionBox>();
        collisionBoxScript.parentBehavior = this;

        // 保存Transform引用
        collisionBoxTransform = collisionBox.transform;

        // 设置初始缩放
        collisionBox.transform.localScale = Vector3.one * initialScale;

        // 设置Layer
        collisionBox.layer = LayerMask.NameToLayer("Terrain");

        Log.Info($"碰撞箱已创建 - 世界位置: {collisionBox.transform.position}, 本地位置: {collisionBox.transform.localPosition}, 初始缩放: {initialScale}");
    }

    /// <summary>
    /// 创建 Control FSM
    /// </summary>
    private void CreateControlFSM()
    {
        if (controlFSM != null)
        {
            Log.Warn("Control FSM 已存在");
            return;
        }

        // 创建 FSM 组件
        controlFSM = gameObject.AddComponent<PlayMakerFSM>();
        controlFSM.FsmName = "Big Silk Ball Control";

        // 创建所有状态
        var initState = CreateInitState();
        var followBossState = CreateFollowBossState();
        var absorbChargeState = CreateAbsorbChargeState();
        var shootWavesState = CreateShootWavesState();
        var finalBurstState = CreateFinalBurstState();
        var destroyState = CreateDestroyState();

        // 设置状态到 FSM
        controlFSM.Fsm.States = new FsmState[]
        {
                initState,
                followBossState,
                absorbChargeState,
                shootWavesState,
                finalBurstState,
                destroyState
        };

        // 注册所有事件
        RegisterFSMEvents();

        // 创建 FSM 变量
        CreateFSMVariables();

        // 添加状态动作
        AddInitActions(initState);
        AddFollowBossActions(followBossState);
        AddAbsorbChargeActions(absorbChargeState);
        AddShootWavesActions(shootWavesState);
        AddFinalBurstActions(finalBurstState);
        AddDestroyActions(destroyState);

        // 添加状态转换
        AddInitTransitions(initState, followBossState);
        AddFollowBossTransitions(followBossState, absorbChargeState);
        AddAbsorbChargeTransitions(absorbChargeState, shootWavesState);
        AddShootWavesTransitions(shootWavesState, finalBurstState);
        AddFinalBurstTransitions(finalBurstState, destroyState);

        // 初始化 FSM 数据和事件
        controlFSM.Fsm.InitData();

        // 设置初始状态
        var fsmType = controlFSM.Fsm.GetType();
        var startedField = fsmType.GetField("started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (startedField != null)
        {
            startedField.SetValue(controlFSM.Fsm, true);
        }

        controlFSM.Fsm.SetState(initState.Name);
        Log.Info($"=== 大丝球 Control FSM 创建完成，当前状态: {controlFSM.Fsm.ActiveStateName} ===");
    }

    #region FSM 事件和变量注册
    /// <summary>
    /// 注册 FSM 事件
    /// </summary>
    private void RegisterFSMEvents()
    {
        startChargeEvent = FsmEvent.GetFsmEvent("START CHARGE");
        animationCompleteEvent = FsmEvent.GetFsmEvent("ANIMATION COMPLETE");

        var events = controlFSM!.Fsm.Events.ToList();
        if (!events.Contains(startChargeEvent)) events.Add(startChargeEvent);
        if (!events.Contains(animationCompleteEvent)) events.Add(animationCompleteEvent);

        controlFSM.Fsm.Events = events.ToArray();
        Log.Info("FSM 事件注册完成");
    }

    /// <summary>
    /// 创建 FSM 变量
    /// </summary>
    private void CreateFSMVariables()
    {
        // GameObject 变量
        bossTransformVar = new FsmGameObject("Boss Transform") { Value = bossObject };
        controlFSM!.FsmVariables.GameObjectVariables = new FsmGameObject[] { bossTransformVar };

        // Vector3 变量
        chestOffsetVar = new FsmVector3("Chest Offset") { Value = chestOffset };
        controlFSM.FsmVariables.Vector3Variables = new FsmVector3[] { chestOffsetVar };

        // Float 变量
        maxScaleVar = new FsmFloat("Max Scale") { Value = maxScale };
        controlFSM.FsmVariables.FloatVariables = new FsmFloat[] { maxScaleVar };

        // Int 变量（可以为空）
        controlFSM.FsmVariables.IntVariables = new FsmInt[] { };

        controlFSM.FsmVariables.Init();
    }
    #endregion

    #region 创建状态
    private FsmState CreateInitState()
    {
        return CreateState(controlFSM!.Fsm, "Init", "初始化状态");
    }

    private FsmState CreateFollowBossState()
    {
        return CreateState(controlFSM!.Fsm, "Follow Boss", "跟随Boss胸前");
    }

    private FsmState CreateAbsorbChargeState()
    {
        return CreateState(controlFSM!.Fsm, "Absorb Charge", "吸收蓄力");
    }

    private FsmState CreateShootWavesState()
    {
        return CreateState(controlFSM!.Fsm, "Shoot Waves", "抛射波次");
    }

    private FsmState CreateFinalBurstState()
    {
        return CreateState(controlFSM!.Fsm, "Final Burst", "最终爆炸");
    }

    private FsmState CreateDestroyState()
    {
        return CreateState(controlFSM!.Fsm, "Destroy", "销毁自身");
    }
    #endregion

    #region 添加状态动作
    private void AddInitActions(FsmState initState)
    {
        // 使用 CallMethod 来设置初始缩放，避免 SetScale 动作的空引用问题
        var setInitialScaleAction = new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("SetInitialScale") { Value = "SetInitialScale" },
            parameters = new FsmVar[0]
        };

        // 等待一帧
        var waitAction = new Wait
        {
            time = new FsmFloat(0.1f),
            finishEvent = FsmEvent.Finished
        };

        initState.Actions = new FsmStateAction[] { setInitialScaleAction, waitAction };
    }

    private void AddFollowBossActions(FsmState followBossState)
    {
        // 使用通用跟随 Action
        var followAction = new Actions.FollowTargetAction
        {
            targetTransform = bossObject?.transform,
            positionOffset = new FsmVector3 { Value = chestOffset },
            followX = new FsmBool { Value = true },
            followY = new FsmBool { Value = true },
            followZ = new FsmBool { Value = false },
            followRotation = new FsmBool { Value = false },
            forceDirectSet = new FsmBool { Value = true }
        };

        followBossState.Actions = new FsmStateAction[] { followAction };
    }

    private void AddAbsorbChargeActions(FsmState absorbChargeState)
    {
        // 1. 开始吸收蓄力协程
        var absorbAction = new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("StartAbsorbChargeCoroutine") { Value = "StartAbsorbChargeCoroutine" },
            parameters = new FsmVar[0]
        };

        // 2. 跟随BOSS动作（持续性，每帧更新）
        var followAction = new Actions.FollowTargetAction
        {
            targetTransform = bossObject?.transform,
            positionOffset = new FsmVector3 { Value = chestOffset },
            followX = new FsmBool { Value = true },
            followY = new FsmBool { Value = true },
            followZ = new FsmBool { Value = false },
            followRotation = new FsmBool { Value = false },
            forceDirectSet = new FsmBool { Value = true }
        };

        // 同时执行：吸收蓄力 + 跟随BOSS
        absorbChargeState.Actions = new FsmStateAction[] { absorbAction, followAction };
    }

    private void AddShootWavesActions(FsmState shootWavesState)
    {
        // 固定位置（停止跟随）
        var fixPositionAction = new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("FixPosition") { Value = "FixPosition" },
            parameters = new FsmVar[0]
        };

        // 开始抛射波次协程
        var shootAction = new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("StartShootWavesCoroutine") { Value = "StartShootWavesCoroutine" },
            parameters = new FsmVar[0]
        };

        // 等待射击波次完成（7波抛射总时长 + 缓冲）
        var waitAction = new Wait
        {
            time = new FsmFloat(6f),  // 4.4秒射击 + 缓冲
            finishEvent = FsmEvent.Finished
        };

        shootWavesState.Actions = new FsmStateAction[] { fixPositionAction, shootAction, waitAction };
    }

    private void AddFinalBurstActions(FsmState finalBurstState)
    {
        // 开始最终爆炸协程
        var burstAction = new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("StartFinalBurstCoroutine") { Value = "StartFinalBurstCoroutine" },
            parameters = new FsmVar[0]
        };

        // 等待协程完成（给足够时间让所有小丝球爆发并飞出）
        var waitAction = new Wait
        {
            time = new FsmFloat(8f),  // 等待爆炸完成（实际约2秒 + 6秒缓冲让小丝球飞行和动画播放完整）
            finishEvent = FsmEvent.Finished
        };

        finalBurstState.Actions = new FsmStateAction[] { burstAction, waitAction };
    }

    private void AddDestroyActions(FsmState destroyState)
    {
        var destroyAction = new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString("DestroySelf") { Value = "DestroySelf" },
            parameters = new FsmVar[0]
        };

        destroyState.Actions = new FsmStateAction[] { destroyAction };
    }
    #endregion

    #region 添加状态转换
    private void AddInitTransitions(FsmState initState, FsmState followBossState)
    {
        initState.Transitions = new FsmTransition[]
        {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Follow Boss",
                    toFsmState = followBossState
                }
        };
    }

    private void AddFollowBossTransitions(FsmState followBossState, FsmState absorbChargeState)
    {
        followBossState.Transitions = new FsmTransition[]
        {
                new FsmTransition
                {
                    FsmEvent = startChargeEvent,
                    toState = "Absorb Charge",
                    toFsmState = absorbChargeState
                }
        };
    }

    private void AddAbsorbChargeTransitions(FsmState absorbChargeState, FsmState shootWavesState)
    {
        absorbChargeState.Transitions = new FsmTransition[]
        {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Shoot Waves",
                    toFsmState = shootWavesState
                }
        };
    }

    private void AddShootWavesTransitions(FsmState shootWavesState, FsmState finalBurstState)
    {
        shootWavesState.Transitions = new FsmTransition[]
        {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Final Burst",
                    toFsmState = finalBurstState
                }
        };
    }

    private void AddFinalBurstTransitions(FsmState finalBurstState, FsmState destroyState)
    {
        finalBurstState.Transitions = new FsmTransition[]
        {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Destroy",
                    toFsmState = destroyState
                }
        };
    }
    #endregion

    #region 辅助方法（供FSM调用）
    /// <summary>
    /// 设置初始缩放（操作heart而不是根物品）
    /// </summary>
    public void SetInitialScale()
    {
        if (heartTransform != null)
        {
            targetHeartScale = Vector3.one * initialScale;
            forceOverrideScale = true;  // 启用缩放覆盖，防止Animator干扰
            Log.Info($"设置heart初始缩放: {initialScale}（启用强制覆盖）");
        }
        else
        {
            Log.Warn("heartTransform为null，无法设置初始缩放");
        }
    }

    /// <summary>
    /// 固定位置（停止跟随Boss）
    /// </summary>
    public void FixPosition()
    {
        // 保存当前heart的缩放，防止Animator重置
        // 根物品停止移动，heart的位置自然跟随根物品，只需要固定缩放

        if (heartTransform != null)
        {
            targetHeartScale = heartTransform.localScale;
            forceOverrideScale = true;     // 继续覆盖heart缩放

            Log.Info($"固定状态 - 根物品位置: {transform.position}, heart缩放: {targetHeartScale}");
        }
        else
        {
            Log.Info($"固定根物品位置: {transform.position}");
        }
    }

    /// <summary>
    /// 开始吸收蓄力协程
    /// </summary>
    public void StartAbsorbChargeCoroutine()
    {
        Log.Info("开始吸收蓄力阶段");
        StartCoroutine(AbsorbChargeCoroutine());
    }

    /// <summary>
    /// 吸收蓄力协程：生成追踪大丝球的小丝球，并等待吸收到最大
    /// </summary>
    private IEnumerator AbsorbChargeCoroutine()
    {
        isAbsorbing = true;
        absorbedCount = 0;
        currentScale = initialScale;
        shouldStopSpawning = false;

        // 启用强制覆盖缩放
        forceOverrideScale = true;
        targetHeartScale = Vector3.one * currentScale;

        // 同时更新碰撞箱缩放
        if (collisionBoxScript != null)
        {
            collisionBoxScript.SetScale(currentScale);
        }

        Log.Info($"吸收蓄力开始 - 初始缩放: {currentScale}, 目标吸收数: {absorbCountToMax}, 每秒生成: {absorbSpawnRate}");

        float elapsed = 0f;
        float spawnInterval = 1f / absorbSpawnRate;  // 生成间隔
        float nextSpawnTime = 0f;

        // 持续生成小丝球，直到达到最大或超时
        while (elapsed < absorbDuration && !shouldStopSpawning)
        {
            elapsed += Time.deltaTime;

            // 定时生成小丝球
            if (elapsed >= nextSpawnTime)
            {
                SpawnAbsorbBall();
                nextSpawnTime = elapsed + spawnInterval;
            }

            yield return null;
        }

        isAbsorbing = false;
        Log.Info($"吸收蓄力完成 - 共吸收: {absorbedCount} 个，最终缩放: {currentScale}");

        // 播放爆炸动画
        PlayBurstAnimation();

        // 通知PhaseControl蓄力完成
        NotifyPhaseControl("ChargeComplete");

        // 发送完成事件到FSM
        if (controlFSM != null)
        {
            controlFSM.SendEvent("FINISHED");
        }
    }

    /// <summary>
    /// 播放爆炸动画
    /// </summary>
    private void PlayBurstAnimation()
    {
        if (animator != null)
        {
            Log.Info($"播放爆炸动画: {burstAnimationName}");
            animator.Play(burstAnimationName);

            // 验证动画是否成功播放
            var clips = animator.runtimeAnimatorController?.animationClips;
            if (clips != null)
            {
                var targetClip = clips.FirstOrDefault(c => c.name == burstAnimationName);
                if (targetClip != null)
                {
                    Log.Info($"动画长度: {targetClip.length:F2}s");
                }
                else
                {
                    Log.Warn($"未找到动画片段: {burstAnimationName}");
                }
            }
        }
        else
        {
            Log.Warn("Animator 为 null，无法播放动画");
        }
    }

    /// <summary>
    /// 处理吸收单个小丝球
    /// </summary>
    public void OnAbsorbBall(SilkBallBehavior silkBall)
    {
        // 检查小丝球是否可以被吸收
        if (silkBall == null || !silkBall.canBeAbsorbed)
        {
            return;
        }

        if (!isAbsorbing)
        {
            // 吸收阶段已结束，但仍然回收还没来得及吸收的小丝球
            silkBall.RecycleToPoolWithZTransition();
            return;
        }

        if (shouldStopSpawning)
        {
            // 已经达到最大，不再增加缩放
            // 关键修复：立即回收这个丝球，不让它继续存在
            Log.Info($"已达到最大缩放，立即回收超额丝球");
            silkBall.RecycleToPoolWithZTransition();
            return;
        }

        // 增加计数
        absorbedCount++;

        // 增加缩放
        currentScale += scaleIncreasePerAbsorb;
        if (currentScale >= maxScale)
        {
            currentScale = maxScale;
            shouldStopSpawning = true;
            Log.Info($"达到最大缩放 {maxScale}，停止生成新的小丝球");
        }

        // 更新heart缩放
        targetHeartScale = Vector3.one * currentScale;

        // 更新碰撞箱缩放
        if (collisionBoxScript != null)
        {
            collisionBoxScript.SetScale(currentScale);
        }

        Log.Info($"吸收小丝球 #{absorbedCount} - 当前缩放: {currentScale:F2}");

        // 播放吸收音效（使用小丝球的 Disappear 音效）
        PlayAbsorbAudio();

        // 回收小丝球到对象池（Z轴过渡动画）
        silkBall.RecycleToPoolWithZTransition();
    }

    /// <summary>
    /// 生成追踪大丝球的小丝球
    /// </summary>
    private void SpawnAbsorbBall()
    {
        if (silkBallManager == null || collisionBoxTransform == null) return;

        // 计算生成位置（半径40范围，70%概率在下半部分）
        float angle;
        if (Random.value < lowerHalfProbability)
        {
            // 下半部分：-90度到90度
            angle = Random.Range(-90f, 90f);
        }
        else
        {
            // 上半部分：90度到270度
            angle = Random.Range(90f, 270f);
        }

        float angleRad = angle * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

        // 生成位置（从碰撞箱的世界位置计算）
        Vector3 spawnPosition = collisionBoxTransform.position + new Vector3(direction.x, direction.y, 0f) * absorbSpawnRadius;

        // 使用统一版本的丝球
        var behavior = silkBallManager.SpawnSilkBall(
            spawnPosition,
            acceleration: 10f,
            maxSpeed: 15f,
            chaseTime: 100f,  // 长时间追踪
            scale: 1f,
            enableRotation: true,
            customTarget: collisionBoxTransform,  // 追踪碰撞箱
            ignoreWall: true,  // 忽略墙壁碰撞
            delayDamageActivation: false
        );

        if (behavior != null)
        {
            // 标记为可被吸收（吸收阶段的小丝球）
            behavior.canBeAbsorbed = true;

            // 启动过期回收机制（15秒后自动回收，防止遗留）
            StartCoroutine(RecycleAbsorbBallAfterTimeout(behavior, 15f));

            // 延迟发送PREPARE和RELEASE事件（等待FSM初始化）
            StartCoroutine(ReleaseAbsorbBall(behavior.gameObject));
        }
    }

    /// <summary>
    /// 延迟释放吸收小丝球（等待FSM初始化）
    /// </summary>
    private IEnumerator ReleaseAbsorbBall(GameObject silkBall)
    {
        // 等待一帧确保FSM完全初始化
        yield return null;

        if (silkBall != null)
        {
            var fsm = silkBall.GetComponent<PlayMakerFSM>();
            if (fsm != null)
            {
                // 先发送PREPARE事件
                fsm.SendEvent("PREPARE");
                yield return new WaitForSeconds(0.1f);
                // 再发送RELEASE事件
                fsm.SendEvent("SILK BALL RELEASE");
            }
        }
    }

    /// <summary>
    /// 过期回收吸收小丝球（防止遗留）
    /// </summary>
    private IEnumerator RecycleAbsorbBallAfterTimeout(SilkBallBehavior behavior, float timeout)
    {
        yield return new WaitForSeconds(timeout);

        // ⚠️ 严格检查：只回收仍然active且canBeAbsorbed的丝球
        // 如果丝球已经被回收（撞墙、撞玩家等），就不要再次回收
        if (behavior != null && behavior.gameObject != null &&
            behavior.isActive && behavior.canBeAbsorbed)
        {
            Log.Info($"吸收小丝球超时 ({timeout}秒)，自动回收");
            behavior.RecycleToPoolWithZTransition();
        }
    }

    /// <summary>
    /// 开始抛射波次协程
    /// </summary>
    public void StartShootWavesCoroutine()
    {
        Log.Info("开始抛射波次阶段");
        StartCoroutine(ShootWavesCoroutine());
    }

    /// <summary>
    /// 抛射波次协程：7波定向抛射，每波配置不同
    /// </summary>
    private IEnumerator ShootWavesCoroutine()
    {
        Log.Info($"抛射波次开始 - 总波数: {_shootWaveConfigs.Length}");
        yield return new WaitForSeconds(1f);
        for (int wave = 0; wave < _shootWaveConfigs.Length; wave++)
        {
            var config = _shootWaveConfigs[wave];

            // 生成这一波的所有小丝球
            for (int i = 0; i < config.ballCount; i++)
            {
                // 延迟从0.4s开始，每个顺延0.1s
                float boostDelay = 0.4f + i * 0.1f;
                SpawnShootBall(config.spawnAngle, config.radiusMult, config.shootAngle, boostDelay);
            }

            // 等待间隔
            yield return new WaitForSeconds(config.interval);
        }

        Log.Info("抛射波次完成");

        // 通知PhaseControl
        NotifyPhaseControl("ShootComplete");

        // 发送完成事件
        if (controlFSM != null)
        {
            controlFSM.SendEvent("FINISHED");
        }
    }

    /// <summary>
    /// 生成定向抛射的小丝球（带随机偏移）
    /// 使用 Has Gravity 状态：先抛射，延迟后施加指向玩家的加速度
    /// </summary>
    /// <param name="spawnAngle">生成位置角度（度，Unity系统：0°=右，90°=上）</param>
    /// <param name="radiusMult">生成半径倍数（0=中心，1=完整半径5）</param>
    /// <param name="shootAngle">抛射方向角度（度）</param>
    /// <param name="boostDelay">加速度延迟时间（秒）</param>
    private void SpawnShootBall(float spawnAngle, float radiusMult, float shootAngle, float boostDelay)
    {
        if (silkBallManager == null || collisionBoxTransform == null) return;

        // 计算生成位置（基于碰撞箱半径5）
        float baseRadius = collisionBoxRadius; // 5f
        float spawnRadius = baseRadius * radiusMult;
        float spawnAngleRad = spawnAngle * Mathf.Deg2Rad;
        Vector2 spawnOffset = new Vector2(Mathf.Cos(spawnAngleRad), Mathf.Sin(spawnAngleRad)) * spawnRadius;
        Vector3 spawnPosition = collisionBoxTransform.position + new Vector3(spawnOffset.x, spawnOffset.y, 0f);

        // 添加角度随机偏移（±shootAngleRandomRange度）
        float angleOffset = Random.Range(-shootAngleRandomRange, shootAngleRandomRange);
        float finalShootAngle = shootAngle + angleOffset;
        float shootAngleRad = finalShootAngle * Mathf.Deg2Rad;
        Vector2 shootDirection = new Vector2(Mathf.Cos(shootAngleRad), Mathf.Sin(shootAngleRad));

        // 添加速度随机偏移（±shootSpeedRandomRange）
        float speedOffset = Random.Range(-shootSpeedRandomRange, shootSpeedRandomRange);
        float finalShootSpeed = shootSpeed + speedOffset;

        // 使用统一版本的丝球（Has Gravity 状态，不持续追踪）
        var behavior = silkBallManager.SpawnSilkBall(
            spawnPosition,
            acceleration: 0f,  // 加速度为0，不持续追踪
            maxSpeed: 25f,     // 稍高的最大速度
            chaseTime: 10f,
            scale: 1.33f,
            enableRotation: true,
            delayDamageActivation: false
        );

        if (behavior != null)
        {
            behavior.StartProtectionTime(1f);
            // 设置初始抛射速度和重力
            behavior.SetPhysics(shootDirection * finalShootSpeed, shootGravityScale);
            behavior.SetGravityScale(shootGravityScale);
            // 进入 Has Gravity 状态（不追踪，只受重力影响）
            behavior.SendFsmEvent("PREPARE");
            StartCoroutine(DelayedSendHasGravityEvent(behavior));
            // 延迟后施加指向玩家的加速度
            behavior.ApplyDelayedBoostTowardsHero(boostDelay, 45f);
        }
    }

    /// <summary>
    /// 延迟发送 HAS_GRAVITY 事件
    /// </summary>
    private IEnumerator DelayedSendHasGravityEvent(SilkBallBehavior behavior)
    {
        yield return new WaitForSeconds(0.01f);
        if (behavior != null)
        {
            behavior.SendFsmEvent("HAS_GRAVITY");
        }
    }


    /// <summary>
    /// 开始最终爆炸协程
    /// </summary>
    public void StartFinalBurstCoroutine()
    {
        Log.Info("开始最终爆炸阶段");

        if (silkBallManager != null)
        {
            Log.Info($"[MemoryBigSilkBall] FinalBurst需求数量: {finalBurstRings * ballsPerRing} (圈数:{finalBurstRings}, 每圈:{ballsPerRing})");
            silkBallManager.LogPoolStatus();
        }
        
        // 禁用碰撞箱位置更新，固定位置避免影响小丝球受力计算
        shouldUpdateCollisionBoxPosition = false;        

        if (collisionBoxTransform != null)
        {
            savedBurstCenter = collisionBoxTransform.position;
        }
        else
        {
            savedBurstCenter = transform.position;
        }

        if (collisionBox != null)
        {
            collisionBox.SetActive(false);
        }

        StartCoroutine(FinalBurstCoroutine());
    }

    /// <summary>
    /// 最终爆炸协程：5圈同心圆，每圈生成后立即爆发，奇数圈顺时针、偶数圈逆时针旋转
    /// </summary>
    private IEnumerator FinalBurstCoroutine()
    {
        Log.Info($"最终爆炸开始 - 圈数: {finalBurstRings}, 每圈数量: {ballsPerRing}");

        yield return new WaitForSeconds(2f);
        // 1. 先全部生成所有圈的小丝球，保存每一圈
        var allRingsBalls = new System.Collections.Generic.List<System.Collections.Generic.List<SilkBallBehavior>>();
        for (int ring = 0; ring < finalBurstRings && ring < ringRadii.Length; ring++)
        {
            float radius = ringRadii[ring];
            float angleStep = 360f / ballsPerRing;
            float angleOffset = (ring % 2 == 1) ? (angleStep / 2f) : 0f;
            var currentRingBalls = new System.Collections.Generic.List<SilkBallBehavior>();
            for (int i = 0; i < ballsPerRing; i++)
            {
                float angle = i * angleStep + angleOffset;
                var behavior = SpawnRingBall(radius, angle);
                if (behavior != null)
                {
                    currentRingBalls.Add(behavior);
                }

                if ((i + 1) % 2 == 0)
                {
                    yield return null;
                }
            }
            allRingsBalls.Add(currentRingBalls);
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(ringBurstDelay); // 爆发前的统一延迟，可根据需求加/删
                                                         // 2. 依次爆发每一圈
        for (int ring = 0; ring < allRingsBalls.Count; ring++)
        {
            var currentRingBalls = allRingsBalls[ring];
            foreach (var behavior in currentRingBalls)
            {
                if (behavior != null)
                {
                    BurstRingBall(behavior, ring);
                }
            }
            if (ring < allRingsBalls.Count - 1)
            {
                yield return new WaitForSeconds(ringSpawnInterval); // 按原始设定间隔爆发
            }
        }

        Log.Info("所有圈已爆发完成");
        NotifyPhaseControl("BurstComplete");
        if (controlFSM != null)
        {
            controlFSM.SendEvent("FINISHED");
        }
    }

    /// <summary>
    /// 在圆环上生成小丝球（静止状态）
    /// </summary>
    /// <returns>生成的小丝球Behavior</returns>
    private SilkBallBehavior? SpawnRingBall(float radius, float angle)
    {
        if (silkBallManager == null) return null;

        float angleRad = angle * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

        Vector3 spawnPosition = savedBurstCenter + new Vector3(direction.x, direction.y, 0f) * radius;

        // 使用统一版本的丝球
        var behavior = silkBallManager.SpawnSilkBall(
            spawnPosition,
            acceleration: 0f,
            maxSpeed: burstSpeed,
            chaseTime: 10f,
            scale: 1f,
            enableRotation: true,
            delayDamageActivation: false
        );

        if (behavior != null)
        {
            behavior.StartProtectionTime(2f);
            behavior.SetPhysics(Vector2.zero, finalBurstGravityScale);
            behavior.SendFsmEvent("SILK BALL RELEASE");
            return behavior;
        }

        return null;
    }

    /// <summary>
    /// 给圆环小丝球施加径向速度，同时设置公转参数
    /// </summary>
    /// <param name="behavior">小丝球Behavior</param>
    /// <param name="ringIndex">圈索引（0=第一圈，1=第二圈...）</param>
    private void BurstRingBall(SilkBallBehavior behavior, int ringIndex)
    {
        if (behavior == null) return;

        // 计算径向方向（使用保存的圆心位置）
        Vector3 direction = (behavior.transform.position - savedBurstCenter).normalized;

        // 根据圈索引计算速度倍数
        float speedMultiplier = 1f;
        if (ringIndex == 0)
        {
            speedMultiplier = innerRingSpeedMultiplier;
        }
        else if (ringIndex >= finalBurstRings - 1)
        {
            speedMultiplier = outerRingSpeedMultiplier;
        }

        // 设置初始径向速度
        Vector2 radialVelocity = new Vector2(direction.x, direction.y) * burstSpeed * speedMultiplier;
        behavior.SetPhysics(radialVelocity, finalBurstGravityScale);

        // 设置公转参数
        bool isClockwise = (ringIndex % 2 == 0);
        float angularSpeed = isClockwise ? -orbitalAngularSpeed : orbitalAngularSpeed;
        behavior.StartOrbitalMotion(savedBurstCenter, angularSpeed, burstSpeed * speedMultiplier);
    }

    /// <summary>
    /// 通知PhaseControl事件
    /// </summary>
    private void NotifyPhaseControl(string eventName)
    {
        if (phaseControlBehavior != null)
        {
            Log.Info($"通知 PhaseControl: {eventName}");
            phaseControlBehavior.OnBigSilkBallEvent(eventName);
        }
        else
        {
            Log.Warn("PhaseControlBehavior 为 null，无法发送通知");
        }
    }

    /// <summary>
    /// 销毁自身
    /// </summary>
    public void DestroySelf()
    {
        Log.Info("销毁大丝球");
        Destroy(gameObject);
    }

    /// <summary>
    /// 发送开始蓄力事件（从外部调用）
    /// </summary>
    public void StartCharge()
    {
        if (controlFSM != null)
        {
            Log.Info($"发送 START CHARGE 事件，当前状态: {controlFSM.ActiveStateName}");
            controlFSM.SendEvent("START CHARGE");
            Log.Info($"事件发送后状态: {controlFSM.ActiveStateName}");
        }
        else
        {
            Log.Error("controlFSM 为 null，无法发送 START CHARGE 事件");
        }
    }

    /// <summary>
    /// 分析 Animator 组件的所有信息
    /// </summary>
    private void AnalyzeAnimator()
    {
        if (animator == null)
        {
            Log.Warn("Animator 组件为 null，无法分析");
            return;
        }

        Log.Info("=== Animator 组件分析 ===");
        Log.Info($"GameObject: {gameObject.name}");
        Log.Info($"Enabled: {animator.enabled}");
        Log.Info($"RuntimeAnimatorController: {animator.runtimeAnimatorController?.name ?? "null"}");

        // 分析参数
        Log.Info("--- Animator 参数 ---");
        if (animator.parameterCount > 0)
        {
            for (int i = 0; i < animator.parameterCount; i++)
            {
                var param = animator.GetParameter(i);
                string valueStr = "";

                switch (param.type)
                {
                    case UnityEngine.AnimatorControllerParameterType.Float:
                        valueStr = $"Float = {animator.GetFloat(param.nameHash)}";
                        break;
                    case UnityEngine.AnimatorControllerParameterType.Int:
                        valueStr = $"Int = {animator.GetInteger(param.nameHash)}";
                        break;
                    case UnityEngine.AnimatorControllerParameterType.Bool:
                        valueStr = $"Bool = {animator.GetBool(param.nameHash)}";
                        break;
                    case UnityEngine.AnimatorControllerParameterType.Trigger:
                        valueStr = "Trigger";
                        break;
                }

                Log.Info($"  [{i}] {param.name} ({param.type}) - {valueStr}");
            }
        }
        else
        {
            Log.Info("  无参数");
        }

        // 分析层
        Log.Info("--- Animator 层 ---");
        Log.Info($"LayerCount: {animator.layerCount}");
        for (int i = 0; i < animator.layerCount; i++)
        {
            string layerName = animator.GetLayerName(i);
            float layerWeight = animator.GetLayerWeight(i);
            Log.Info($"  [{i}] {layerName} - Weight: {layerWeight}");

            // 当前状态信息
            var currentState = animator.GetCurrentAnimatorStateInfo(i);
            Log.Info($"      当前状态: Hash={currentState.shortNameHash}, NormalizedTime={currentState.normalizedTime:F2}, Length={currentState.length:F2}s");
        }

        // 动画片段信息
        Log.Info("--- 可用动画片段 ---");
        if (animator.runtimeAnimatorController != null)
        {
            var clips = animator.runtimeAnimatorController.animationClips;
            if (clips != null && clips.Length > 0)
            {
                Log.Info($"总动画片段数: {clips.Length}");
                foreach (var clip in clips)
                {
                    Log.Info($"  - {clip.name} (Length: {clip.length:F2}s, FPS: {clip.frameRate})");

                    // 检查是否是爆炸动画
                    if (clip.name.Contains("Burst") || clip.name.Contains("burst") ||
                        clip.name.Contains("Intro"))
                    {
                        Log.Info($"    ★ 可能的爆炸动画！");
                    }
                }
            }
            else
            {
                Log.Info("  无动画片段");
            }
        }

        // 当前播放信息
        Log.Info("--- 当前播放状态 ---");
        Log.Info($"Speed: {animator.speed}");
    }
    /// <summary>
    /// 提取大丝球吸收小丝球时使用的音效资源
    /// 通过实例化预制体来避免 FSM 未初始化问题
    /// </summary>
    private void ExtractAbsorbAudioAction()
    {
        GameObject? tempInstance = null;
        try
        {
            // 1. 从 AssetManager 获取预制体
            GameObject? sourcePrefab = null;
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                var assetManager = managerObj.GetComponent<AssetManager>();
                if (assetManager != null)
                {
                    sourcePrefab = assetManager.Get<GameObject>("Reaper Silk Bundle");
                }
            }

            // 2. 回退到 SilkBallManager 的自定义预制体
            if (sourcePrefab == null && silkBallManager?.CustomSilkBallPrefab != null)
            {
                sourcePrefab = silkBallManager.CustomSilkBallPrefab;
            }

            if (sourcePrefab == null)
            {
                Log.Warn("无法提取吸收音效：找不到预制体");
                return;
            }

            // 3. 临时实例化预制体（禁用状态，避免执行逻辑）
            tempInstance = Instantiate(sourcePrefab);
            tempInstance.SetActive(false);
            tempInstance.name = "TempSilkBallForAudioExtraction";

            Log.Info($"临时实例化预制体用于提取音效: {tempInstance.name}");

            // 4. 等待一帧让 FSM 初始化
            StartCoroutine(ExtractAudioAfterFrame(tempInstance));
        }
        catch (System.Exception ex)
        {
            Log.Error($"提取吸收音效失败：{ex.Message}");
            if (tempInstance != null)
            {
                Destroy(tempInstance);
            }
        }
    }

    /// <summary>
    /// 等待一帧后提取音效数据（确保 FSM 已初始化）
    /// </summary>
    private IEnumerator ExtractAudioAfterFrame(GameObject tempInstance)
    {
        // 等待一帧，确保 FSM 完全初始化
        yield return null;

        try
        {
            if (tempInstance == null)
            {
                Log.Warn("临时实例已被销毁，无法提取音效");
                yield break;
            }

            // 查找 Control FSM
            var controlFsm = tempInstance.GetComponents<PlayMakerFSM>()
                .FirstOrDefault(fsm => fsm.FsmName == "Control");

            if (controlFsm == null)
            {
                Log.Warn("无法提取吸收音效：临时实例上未找到 Control FSM");
                yield break;
            }

            // 查找 Disappear 状态
            var disappearState = controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Disappear");
            if (disappearState == null)
            {
                Log.Warn("无法提取吸收音效：Control FSM 中未找到 Disappear 状态");
                yield break;
            }

            // 在 Disappear 状态中查找 PlayAudioEvent 动作
            foreach (var action in disappearState.Actions)
            {
                if (action is PlayAudioEvent audioAction)
                {
                    // 提取音频资源引用
                    absorbAudioClip = audioAction.audioClip?.Value as AudioClip;
                    absorbAudioPitchMin = audioAction.pitchMin?.Value ?? 1f;
                    absorbAudioPitchMax = audioAction.pitchMax?.Value ?? 1f;
                    absorbAudioVolume = audioAction.volume?.Value ?? 1f;

                    break;
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error($"提取音效数据时出错：{ex.Message}");
        }
        finally
        {
            // 销毁临时实例
            if (tempInstance != null)
            {
                Destroy(tempInstance);
                Log.Debug("已销毁临时音效提取实例");
            }
        }
    }
    private void PlayAbsorbAudio()
    {
        if (absorbAudioClip == null)
        {
            return;
        }
        try
        {
            // 方案1：直接在英雄位置播放 2D 音效（推荐）
            var hero = HeroController.instance;
            if (hero != null)
            {
                float finalVolume = absorbAudioVolume * absorbAudioVolumeMultiplier;
                float randomPitch = UnityEngine.Random.Range(absorbAudioPitchMin, absorbAudioPitchMax);
                var audioSource = hero.GetComponent<AudioSource>();

                if (audioSource != null)
                {
                    // 使用英雄的 AudioSource 播放
                    audioSource.pitch = randomPitch;
                    audioSource.PlayOneShot(absorbAudioClip, finalVolume);
                    Log.Debug($"通过英雄播放吸收音效: {absorbAudioClip.name}, pitch={randomPitch:F2}");
                }
                else
                {
                    // 备用方案：在摄像机位置播放
                    AudioSource.PlayClipAtPoint(absorbAudioClip, Camera.main.transform.position, finalVolume);
                    Log.Debug($"通过摄像机播放吸收音效: {absorbAudioClip.name}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error($"播放吸收音效失败：{ex.Message}");
        }
    }
    #endregion
}



