using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// Bomb Blast 行为组件 - 管理爆炸的 FSM 补丁、大小调整和丝球环生成
    /// 
    /// 功能：
    /// - FSM 补丁：移除 RecycleSelf，添加 CallMethod 回收
    /// - 大小调整：根据 isBurstBlast 设置更大的爆炸尺寸
    /// - 丝球环生成：在爆炸位置生成反向加速度丝球环
    /// </summary>
    public class BombBlastBehavior : MonoBehaviour
    {
        #region 配置参数
        /// <summary>是否为爆炸连段（更大尺寸）</summary>
        public bool isBurstBlast = false;

        /// <summary>爆炸连段的尺寸倍数</summary>
        public float burstSizeMultiplier = 1.5f;

        /// <summary>是否生成丝球环</summary>
        public bool spawnSilkBallRing = false;

        /// <summary>丝球数量</summary>
        public int silkBallCount = 8;

        /// <summary>丝球初始向外速度</summary>
        public float initialOutwardSpeed = 12f;

        /// <summary>反向加速度值</summary>
        public float reverseAcceleration = 25f;

        /// <summary>最大向内速度</summary>
        public float maxInwardSpeed = 30f;

        /// <summary>反向加速度持续时间</summary>
        public float reverseAccelDuration = 5f;

        /// <summary>丝球环模式：true=反向加速度，false=径向爆发</summary>
        public bool useReverseAccelMode = true;

        /// <summary>径向爆发速度</summary>
        public float radialBurstSpeed = 18f;

        /// <summary>释放前的等待时间</summary>
        public float releaseDelay = 0.3f;
        #endregion

        #region 内部引用
        private PlayMakerFSM? _controlFsm;
        private FWBlastManager? _blastManager;
        private SilkBallManager? _silkBallManager;
        private FsmFloat? _sizeVar;
        private bool _fsmPatched = false;
        private bool _isActive = false;
        #endregion

        #region 生命周期
        private void Awake()
        {
            _controlFsm = GetComponent<PlayMakerFSM>();
            GetManagerReferences();
        }

        private void Start()
        {
            if (_controlFsm != null && !_fsmPatched)
            {
                // 输出补丁前的 FSM 报告
                string prePatchPath = FSM_OUTPUT_PATH + "_bombBlast_prePatch.txt";
                FsmAnalyzer.WriteFsmReport(_controlFsm, prePatchPath);
                Log.Debug($"[BombBlastBehavior] 补丁前 FSM 报告已输出: {prePatchPath}");

                PatchFsm();
                _fsmPatched = true;

                // 输出补丁后的 FSM 报告
                string postPatchPath = FSM_OUTPUT_PATH + "_bombBlast_postPatch.txt";
                FsmAnalyzer.WriteFsmReport(_controlFsm, postPatchPath);
                Log.Debug($"[BombBlastBehavior] 补丁后 FSM 报告已输出: {postPatchPath}");
            }
        }

        /// <summary>FSM 输出路径</summary>
        private const string FSM_OUTPUT_PATH = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\";

        private void OnEnable()
        {
            _isActive = true;
        }

        private void OnDisable()
        {
            _isActive = false;
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 配置爆炸参数（由 FWBlastManager.SpawnBombBlast 调用）
        /// </summary>
        public void Configure(bool burst, bool spawnRing, int ballCount = 8,
            float outSpeed = 12f, float reverseAccel = 25f, float maxInSpeed = 30f, float duration = 5f,
            bool useReverseAccel = true, float radialSpeed = 18f, float delay = 0.3f)
        {
            isBurstBlast = burst;
            spawnSilkBallRing = spawnRing;
            silkBallCount = ballCount;
            initialOutwardSpeed = outSpeed;
            reverseAcceleration = reverseAccel;
            maxInwardSpeed = maxInSpeed;
            reverseAccelDuration = duration;
            useReverseAccelMode = useReverseAccel;
            radialBurstSpeed = radialSpeed;
            releaseDelay = delay;

            // 调整爆炸大小
            if (isBurstBlast && _sizeVar != null)
            {
                _sizeVar.Value *= burstSizeMultiplier;
            }
        }

        /// <summary>
        /// 触发爆炸效果（由 FSM 的 Blast 状态调用）
        /// </summary>
        public void OnBlastTriggered()
        {
            if (spawnSilkBallRing && _silkBallManager != null)
            {
                StartCoroutine(SpawnSilkBallRingCoroutine());
            }
        }

        /// <summary>
        /// 回收爆炸（由 FSM 调用，替代原 RecycleSelf）
        /// </summary>
        public void RecycleBlast()
        {
            if (_blastManager != null)
            {
                _blastManager.RecycleBombBlast(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 重置配置（从池中取出时调用）
        /// </summary>
        public void ResetConfig()
        {
            isBurstBlast = false;
            spawnSilkBallRing = false;
            silkBallCount = 8;
            initialOutwardSpeed = 12f;
            reverseAcceleration = 25f;
            maxInwardSpeed = 30f;
            reverseAccelDuration = 5f;
            useReverseAccelMode = true;
            radialBurstSpeed = 18f;
            releaseDelay = 0.3f;
        }
        #endregion

        #region FSM 补丁
        /// <summary>
        /// 补丁 FSM：移除 RecycleSelf，添加 CallMethod 回收
        /// </summary>
        private void PatchFsm()
        {
            if (_controlFsm == null) return;

            // 缓存 Size 变量引用
            _sizeVar = _controlFsm.FsmVariables.GetFsmFloat("Size");

            // 修改 End 状态：移除 RecycleSelf，添加 CallMethod
            var endState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "End");
            if (endState != null)
            {
                var actions = endState.Actions.ToList();

                // 移除 RecycleSelf
                actions.RemoveAll(a => a is RecycleSelf);

                // 添加 CallMethod 调用 RecycleBlast
                var callMethod = new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("RecycleBlast") { Value = "RecycleBlast" },
                    parameters = new FsmVar[0],
                    storeResult = new FsmVar()
                };
                actions.Add(callMethod);

                endState.Actions = actions.ToArray();
                Log.Debug("[BombBlastBehavior] FSM End 状态已补丁：RecycleSelf -> CallMethod(RecycleBlast)");
            }

            // 修改 Wait 状态：在等待结束后调用 OnBlastTriggered（丝球环在爆炸动画完成后生成）
            var waitState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Wait");
            if (waitState != null)
            {
                var actions = waitState.Actions.ToList();

                // 在开头添加 CallMethod 调用 OnBlastTriggered（Wait 状态开始时爆炸动画已完成）
                var callMethod = new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("OnBlastTriggered") { Value = "OnBlastTriggered" },
                    parameters = new FsmVar[0],
                    storeResult = new FsmVar()
                };
                actions.Insert(0, callMethod);

                waitState.Actions = actions.ToArray();
                Log.Debug("[BombBlastBehavior] FSM Wait 状态已补丁：添加 CallMethod(OnBlastTriggered)");
            }
        }
        #endregion

        #region 丝球环生成
        /// <summary>
        /// 生成丝球环协程
        /// 支持两种模式：
        /// 1. 反向加速度模式：丝球向外飞出后受到指向圆心的加速度，最终返回并撞墙消散
        /// 2. 径向爆发模式：丝球以固定速度径向向外飞出，撞墙消散
        /// </summary>
        private IEnumerator SpawnSilkBallRingCoroutine()
        {
            // 等待一小段时间让爆炸动画开始
            yield return new WaitForSeconds(0.1f);

            if (_silkBallManager == null) yield break;

            Vector3 center = transform.position;
            float angleStep = 360f / silkBallCount;
            float startAngle = Random.Range(0f, 360f);

            // 保存生成的丝球和它们的方向
            var silkBalls = new List<(MemorySilkBallBehavior ball, Vector2 direction)>();

            // 第一阶段：生成所有丝球（处于 Prepare 状态）
            for (int i = 0; i < silkBallCount; i++)
            {
                float angle = startAngle + i * angleStep;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 在中心位置生成丝球
                var silkBall = _silkBallManager.SpawnMemorySilkBall(
                    center,
                    acceleration: 0f,
                    maxSpeed: useReverseAccelMode ? maxInwardSpeed : radialBurstSpeed,
                    chaseTime: reverseAccelDuration,
                    scale: 0.8f,
                    enableRotation: true,
                    customTarget: null,
                    ignoreWall: false  // 不忽略墙壁碰撞，撞墙后消散
                );

                if (silkBall != null)
                {
                    silkBalls.Add((silkBall, direction));

                    // 初始化丝球的 Rigidbody
                    var rb = silkBall.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.gravityScale = 0f;
                        rb.linearVelocity = Vector2.zero;  // 初始静止
                    }

                    // 如果是反向加速度模式，配置参数
                    if (useReverseAccelMode)
                    {
                        silkBall.ConfigureReverseAcceleration(
                            center,
                            reverseAcceleration,
                            initialOutwardSpeed,
                            maxInwardSpeed,
                            reverseAccelDuration
                        );
                    }
                }
            }

            Log.Debug($"[BombBlastBehavior] 已生成 {silkBalls.Count} 个丝球，等待 {releaseDelay} 秒后释放");

            // 第二阶段：等待一段时间
            yield return new WaitForSeconds(releaseDelay);

            // 第三阶段：释放所有丝球
            foreach (var (silkBall, direction) in silkBalls)
            {
                if (silkBall == null || !silkBall.isActive) continue;

                var fsm = silkBall.GetComponent<PlayMakerFSM>();
                var rb = silkBall.GetComponent<Rigidbody2D>();

                if (useReverseAccelMode)
                {
                    // 反向加速度模式：设置初始向外速度，然后启动反向加速度
                    if (rb != null)
                    {
                        rb.linearVelocity = direction * initialOutwardSpeed;
                    }
                    // 通过事件启动反向加速度模式
                    silkBall.StartReverseAccelerationMode();
                }
                else
                {
                    // 径向爆发模式：设置径向速度，使用 Chase Hero 模式但 acceleration=0
                    if (rb != null)
                    {
                        rb.gravityScale = 0f;  // 无重力，纯径向运动
                        rb.linearVelocity = direction * radialBurstSpeed;
                    }
                    // 配置丝球为直线飞行模式：acceleration=0，chaseTime=6s
                    silkBall.acceleration = 0f;
                    silkBall.chaseTime = 6f;
                    // 通过 SILK BALL RELEASE 进入 Chase Hero 状态
                    if (fsm != null)
                    {
                        fsm.SendEvent("SILK BALL RELEASE");
                    }
                }
            }

            Log.Debug($"[BombBlastBehavior] 已释放 {silkBalls.Count} 个丝球，模式: {(useReverseAccelMode ? "反向加速度" : "径向爆发")}");
        }
        #endregion

        #region 辅助方法
        private void GetManagerReferences()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _blastManager = managerObj.GetComponent<FWBlastManager>();
                _silkBallManager = managerObj.GetComponent<SilkBallManager>();
            }
        }
        #endregion
    }
}
