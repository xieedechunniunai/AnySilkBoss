using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 爆炸 + 丝球连段 Action - 在爆炸触发后生成一圈小丝球
    /// 
    /// 功能：
    /// - 在指定位置生成爆炸
    /// - 爆炸后生成 8 个丝球环（间隔 45°）
    /// - 支持两种丝球模式：直射模式 / 反向加速度模式
    /// 
    /// 使用场景：
    /// - 原罪者爆炸联动丝球攻击
    /// - 少量爆炸 + 丝球连段招式
    /// </summary>
    public class BlastWithSilkBallAction : FsmStateAction
    {
        #region 参数配置
        [HutongGames.PlayMaker.Tooltip("爆炸生成位置")]
        public FsmVector3 blastPosition;

        [HutongGames.PlayMaker.Tooltip("是否生成丝球环")]
        public FsmBool spawnSilkBallRing;

        [HutongGames.PlayMaker.Tooltip("丝球数量（默认 8 个，间隔 45°）")]
        public FsmInt silkBallCount;

        [HutongGames.PlayMaker.Tooltip("丝球环半径（从爆炸中心的距离）")]
        public FsmFloat ringRadius;

        [HutongGames.PlayMaker.Tooltip("初始角度偏移（随机范围 0-360）")]
        public FsmFloat angleOffset;

        [HutongGames.PlayMaker.Tooltip("丝球发射速度")]
        public FsmFloat silkBallSpeed;

        [HutongGames.PlayMaker.Tooltip("是否使用反向加速度模式（true=丝球向外后返回，false=直射向外）")]
        public FsmBool useReverseAccelMode;

        [HutongGames.PlayMaker.Tooltip("反向加速度值（仅在 useReverseAccelMode=true 时有效）")]
        public FsmFloat reverseAcceleration;

        [HutongGames.PlayMaker.Tooltip("爆炸生成后的延迟（秒），用于同步丝球生成时机")]
        public FsmFloat silkBallSpawnDelay;

        [HutongGames.PlayMaker.Tooltip("完成后触发的事件")]
        public FsmEvent? onComplete;
        #endregion

        #region 管理器引用
        private FWBlastManager? blastManager;
        private SilkBallManager? silkBallManager;
        #endregion

        public override void Reset()
        {
            blastPosition = Vector3.zero;
            spawnSilkBallRing = true;
            silkBallCount = 8;
            ringRadius = 2f;
            angleOffset = 0f;
            silkBallSpeed = 15f;
            useReverseAccelMode = false;
            reverseAcceleration = 20f;
            silkBallSpawnDelay = 0.3f;
            onComplete = null;
        }

        public override void OnEnter()
        {
            // 获取管理器引用
            GetManagerReferences();

            if (blastManager == null)
            {
                Log("BlastWithSilkBallAction: 未找到 FWBlastManager");
                Finish();
                return;
            }

            // 生成爆炸
            SpawnBlast();

            // 如果需要生成丝球环，启动协程
            if (spawnSilkBallRing.Value && silkBallManager != null)
            {
                // 延迟生成丝球（与爆炸动画同步）
                if (silkBallSpawnDelay.Value > 0)
                {
                    StartCoroutine(SpawnSilkBallRingDelayed());
                }
                else
                {
                    SpawnSilkBallRing();
                    TriggerComplete();
                }
            }
            else
            {
                TriggerComplete();
            }
        }

        /// <summary>
        /// 获取管理器引用
        /// </summary>
        private void GetManagerReferences()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                blastManager = managerObj.GetComponent<FWBlastManager>();
                silkBallManager = managerObj.GetComponent<SilkBallManager>();
            }
        }

        /// <summary>
        /// 生成爆炸
        /// </summary>
        private void SpawnBlast()
        {
            if (blastManager == null) return;

            blastManager.SpawnBombBlast(blastPosition.Value);
        }

        /// <summary>
        /// 延迟生成丝球环
        /// </summary>
        private System.Collections.IEnumerator SpawnSilkBallRingDelayed()
        {
            yield return new WaitForSeconds(silkBallSpawnDelay.Value);
            SpawnSilkBallRing();
            TriggerComplete();
        }

        /// <summary>
        /// 生成丝球环
        /// </summary>
        private void SpawnSilkBallRing()
        {
            if (silkBallManager == null) return;

            int count = silkBallCount.Value;
            if (count <= 0) count = 8;

            float angleStep = 360f / count;
            float startAngle = angleOffset.Value > 0 ? angleOffset.Value : Random.Range(0f, 360f);

            Vector3 center = blastPosition.Value;

            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + i * angleStep;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 计算丝球生成位置
                Vector3 spawnPos = center + new Vector3(direction.x, direction.y, 0) * ringRadius.Value;

                // 生成丝球
                var silkBall = silkBallManager.SpawnMemorySilkBall(
                    spawnPos,
                    acceleration: 0f,  // 不追踪
                    maxSpeed: silkBallSpeed.Value,
                    chaseTime: 10f,
                    scale: 1f,
                    enableRotation: true
                );

                if (silkBall != null)
                {
                    var rb = silkBall.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.gravityScale = 0f;

                        if (useReverseAccelMode.Value)
                        {
                            // 反向加速度模式：设置初速度，后续由状态机处理加速度
                            rb.linearVelocity = direction * silkBallSpeed.Value;
                            
                            // TODO: 需要配合 MemorySilkBallBehavior 的 Reverse Acceleration 状态
                            // 这里设置标记，让丝球进入反向加速度状态
                            // silkBall.SetReverseAccelerationMode(center, reverseAcceleration.Value);
                        }
                        else
                        {
                            // 直射模式：固定速度向外
                            rb.linearVelocity = direction * silkBallSpeed.Value;
                        }

                        // 保护时间，避免刚生成就消失
                        silkBall.StartProtectionTime(0.5f);

                        // 触发 FSM 状态
                        var fsm = silkBall.GetComponent<PlayMakerFSM>();
                        if (fsm != null)
                        {
                            fsm.SendEvent("PREPARE");
                            // 使用重力状态（实际上无重力，只是利用其物理逻辑）
                            fsm.Fsm.SetState("Has Gravity");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 触发完成事件
        /// </summary>
        private void TriggerComplete()
        {
            if (onComplete != null)
            {
                Fsm.Event(onComplete);
            }
            Finish();
        }
    }
}
