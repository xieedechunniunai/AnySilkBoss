using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Behaviours.Memory;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 反向加速度 Action - 实现「初速度向外 + 反向加速度 → 返回」的效果
    /// 
    /// 物理逻辑：
    /// 1. 进入时设置径向初速度 v0（向外）
    /// 2. 每帧施加指向圆心的加速度 a
    /// 3. 当速度方向反转且接近圆心时，可触发下一阶段
    /// 
    /// 使用场景：
    /// - 喷射丝球从圆心向外发射后返回
    /// - 原罪者爆炸炸出的丝球返回效果
    /// </summary>
    public class ReverseAccelerationAction : FsmStateAction
    {
        #region 参数配置
        [HutongGames.PlayMaker.Tooltip("圆心位置（丝球将从这里向外发射并返回）")]
        public FsmVector3 centerPoint;

        [HutongGames.PlayMaker.Tooltip("初始向外速度（单位/秒）")]
        public FsmFloat initialOutwardSpeed;

        [HutongGames.PlayMaker.Tooltip("反向加速度（指向圆心，单位/秒²）")]
        public FsmFloat reverseAcceleration;

        [HutongGames.PlayMaker.Tooltip("最大向内速度（限制返回速度）")]
        public FsmFloat maxInwardSpeed;

        [HutongGames.PlayMaker.Tooltip("返回圆心的距离阈值")]
        public FsmFloat returnThreshold;

        [HutongGames.PlayMaker.Tooltip("运行时间限制（秒），<=0 表示无限")]
        public FsmFloat maxDuration;

        [HutongGames.PlayMaker.Tooltip("到达圆心时触发的事件")]
        public FsmEvent? onReturnToCenter;

        [HutongGames.PlayMaker.Tooltip("超时时触发的事件")]
        public FsmEvent? onTimeout;

        [HutongGames.PlayMaker.Tooltip("是否在碰墙时结束")]
        public FsmBool finishOnWallHit;
        #endregion

        #region 运行时变量
        private Rigidbody2D? rb2d;
        private Transform? selfTransform;
        private float elapsedTime;
        private bool hasReversed;  // 是否已经反转方向（速度从向外变为向内）
        private Vector2 initialDirection;  // 初始方向（从圆心指向物体的方向）
        private Vector2 fixedAccelDirection;  // 固定的加速度方向（指向圆心，初始计算后不再变化）
        #endregion

        public override void Reset()
        {
            centerPoint = Vector3.zero;
            initialOutwardSpeed = 15f;
            reverseAcceleration = 20f;
            maxInwardSpeed = 25f;
            returnThreshold = 0.5f;
            maxDuration = 0f;
            onReturnToCenter = null;
            onTimeout = null;
            finishOnWallHit = true;
        }

        public override void OnEnter()
        {
            if (Owner == null)
            {
                Log("ReverseAccelerationAction: Owner 为 null");
                Finish();
                return;
            }

            selfTransform = Owner.transform;
            rb2d = Owner.GetComponent<Rigidbody2D>();

            if (rb2d == null)
            {
                Log("ReverseAccelerationAction: 未找到 Rigidbody2D");
                Finish();
                return;
            }

            // 重要：ReverseAccelerationAction 的参数对象是在 FSM 构建时创建的。
            // 运行时 ConfigureReverseAcceleration() 修改的是 MemorySilkBallBehavior 的字段，
            // 这里需要在进入状态时从 Owner 读取最新值，避免 centerPoint 仍为默认值导致方向异常。
            var silkBallBehavior = Owner.GetComponent<MemorySilkBallBehavior>();
            if (silkBallBehavior != null)
            {
                centerPoint.Value = silkBallBehavior.reverseAccelCenter;
                initialOutwardSpeed.Value = silkBallBehavior.initialOutwardSpeed;
                reverseAcceleration.Value = silkBallBehavior.reverseAccelValue;
                maxInwardSpeed.Value = silkBallBehavior.maxInwardSpeed;
                maxDuration.Value = silkBallBehavior.reverseAccelDuration;
            }

            elapsedTime = 0f;
            hasReversed = false;

            // 计算初始向外方向：优先使用外部已经设置好的初速度方向（例如 BlastBurst1 径向发射）
            Vector2 outwardDirection;
            if (rb2d.linearVelocity.sqrMagnitude > 0.0001f)
            {
                outwardDirection = rb2d.linearVelocity.normalized;
            }
            else
            {
                Vector2 center = new Vector2(centerPoint.Value.x, centerPoint.Value.y);
                Vector2 selfPos = new Vector2(selfTransform.position.x, selfTransform.position.y);
                outwardDirection = (selfPos - center).normalized;

                // 如果物体就在圆心，随机一个方向
                if (outwardDirection.sqrMagnitude < 0.0001f)
                {
                    float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    outwardDirection = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle));
                }
            }

            initialDirection = outwardDirection;

            // 固定加速度方向（指向圆心，之后不再变化）
            fixedAccelDirection = -initialDirection;

            // 设置初始向外速度
            rb2d.gravityScale = 0f;  // 禁用重力
            rb2d.linearVelocity = initialDirection * initialOutwardSpeed.Value;
        }

        public override void OnUpdate()
        {
            if (rb2d == null || selfTransform == null)
            {
                Finish();
                return;
            }

            // 检查超时
            if (maxDuration.Value > 0)
            {
                elapsedTime += Time.deltaTime;
                if (elapsedTime >= maxDuration.Value)
                {
                    if (onTimeout != null)
                    {
                        Fsm.Event(onTimeout);
                    }
                    Finish();
                    return;
                }
            }

            // 应用固定方向的反向加速度（方向不随位置变化）
            Vector2 accel = fixedAccelDirection * reverseAcceleration.Value;
            rb2d.linearVelocity += accel * Time.deltaTime;

            // 限制最大速度（沿加速度方向）
            float velocityAlongAccel = Vector2.Dot(rb2d.linearVelocity, fixedAccelDirection);
            if (velocityAlongAccel > maxInwardSpeed.Value)
            {
                // 分解速度为平行和垂直于加速度方向的分量
                Vector2 parallelVelocity = fixedAccelDirection * maxInwardSpeed.Value;
                Vector2 perpendicular = new Vector2(-fixedAccelDirection.y, fixedAccelDirection.x);
                float perpVelocity = Vector2.Dot(rb2d.linearVelocity, perpendicular);
                rb2d.linearVelocity = parallelVelocity + perpendicular * perpVelocity;
            }

            // 检测速度是否已反转（沿加速度方向的分量从负变为正）
            if (!hasReversed && velocityAlongAccel > 0)
            {
                hasReversed = true;
            }

            // 仅当外部配置了 onReturnToCenter 时，才启用“回到圆心结束”的逻辑
            if (onReturnToCenter != null && returnThreshold.Value > 0f)
            {
                Vector2 center = new Vector2(centerPoint.Value.x, centerPoint.Value.y);
                Vector2 selfPos = new Vector2(selfTransform.position.x, selfTransform.position.y);
                float distanceToCenter = Vector2.Distance(selfPos, center);
                if (hasReversed && distanceToCenter <= returnThreshold.Value)
                {
                    Fsm.Event(onReturnToCenter);
                    Finish();
                    return;
                }
            }
        }

        public override void OnExit()
        {
            rb2d = null;
            selfTransform = null;
        }

        /// <summary>
        /// 获取当前是否已反转方向
        /// </summary>
        public bool HasReversed => hasReversed;

        /// <summary>
        /// 获取距离圆心的距离
        /// </summary>
        public float GetDistanceToCenter()
        {
            if (selfTransform == null) return float.MaxValue;
            Vector2 center = new Vector2(centerPoint.Value.x, centerPoint.Value.y);
            Vector2 selfPos = new Vector2(selfTransform.position.x, selfTransform.position.y);
            return Vector2.Distance(selfPos, center);
        }
    }
}
