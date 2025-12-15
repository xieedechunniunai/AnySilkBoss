using UnityEngine;
using HutongGames.PlayMaker;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 通用追踪 Action - 让任意 GameObject 追踪任意目标
    /// 从 MemorySilkBallChaseAction 改造，支持更通用的追踪场景
    /// 
    /// 使用场景：
    /// - 丝球追踪玩家/大丝球
    /// - 爆炸汇聚向 Boss
    /// - 飞针跟随目标
    /// </summary>
    public class ChaseTargetAction : FsmStateAction
    {
        #region 参数配置
        [HutongGames.PlayMaker.Tooltip("追踪目标的 Transform（可选，如果不设置则使用 targetGameObject）")]
        public FsmGameObject targetGameObject;

        [HutongGames.PlayMaker.Tooltip("追踪目标的 Transform（直接引用，优先级高于 targetGameObject）")]
        public Transform? targetTransform;

        [HutongGames.PlayMaker.Tooltip("加速度（单位/秒²）")]
        public FsmFloat acceleration;

        [HutongGames.PlayMaker.Tooltip("最大速度（单位/秒）")]
        public FsmFloat maxSpeed;

        [HutongGames.PlayMaker.Tooltip("是否使用刚体移动（true=通过 Rigidbody2D.velocity，false=直接移动 Transform）")]
        public FsmBool useRigidbody;

        [HutongGames.PlayMaker.Tooltip("追踪时间（秒），<=0 表示无限追踪")]
        public FsmFloat chaseTime;

        [HutongGames.PlayMaker.Tooltip("到达目标的距离阈值，到达后触发 onReachTarget 事件")]
        public FsmFloat reachDistance;

        [HutongGames.PlayMaker.Tooltip("到达目标时触发的事件")]
        public FsmEvent? onReachTarget;

        [HutongGames.PlayMaker.Tooltip("追踪超时时触发的事件")]
        public FsmEvent? onTimeout;
        #endregion

        #region 运行时变量
        private Rigidbody2D? rb2d;
        private Transform? selfTransform;
        private Transform? resolvedTarget;
        private float elapsedTime;
        #endregion

        public override void Reset()
        {
            targetGameObject = null;
            targetTransform = null;
            acceleration = 30f;
            maxSpeed = 20f;
            useRigidbody = true;
            chaseTime = 0f;
            reachDistance = 0.5f;
            onReachTarget = null;
            onTimeout = null;
        }

        public override void OnEnter()
        {
            if (Owner == null)
            {
                Log("ChaseTargetAction: Owner 为 null");
                Finish();
                return;
            }

            selfTransform = Owner.transform;
            elapsedTime = 0f;

            // 解析目标
            resolvedTarget = ResolveTarget();
            if (resolvedTarget == null)
            {
                Log("ChaseTargetAction: 未找到追踪目标");
                Finish();
                return;
            }

            // 如果使用刚体，获取 Rigidbody2D
            if (useRigidbody.Value)
            {
                rb2d = Owner.GetComponent<Rigidbody2D>();
                if (rb2d == null)
                {
                    Log("ChaseTargetAction: 需要使用刚体但未找到 Rigidbody2D");
                    Finish();
                    return;
                }
            }
        }

        public override void OnUpdate()
        {
            if (selfTransform == null || resolvedTarget == null)
            {
                Finish();
                return;
            }

            // 检查超时
            if (chaseTime.Value > 0)
            {
                elapsedTime += Time.deltaTime;
                if (elapsedTime >= chaseTime.Value)
                {
                    if (onTimeout != null)
                    {
                        Fsm.Event(onTimeout);
                    }
                    Finish();
                    return;
                }
            }

            // 计算朝向目标的方向
            Vector2 direction = ((Vector2)resolvedTarget.position - (Vector2)selfTransform.position).normalized;

            // 检查是否到达目标
            float distance = Vector2.Distance(selfTransform.position, resolvedTarget.position);
            if (distance <= reachDistance.Value)
            {
                if (onReachTarget != null)
                {
                    Fsm.Event(onReachTarget);
                }
                Finish();
                return;
            }

            // 应用移动
            if (useRigidbody.Value && rb2d != null)
            {
                // 使用刚体：应用加速度
                Vector2 accelerationForce = direction * acceleration.Value;
                rb2d.linearVelocity += accelerationForce * Time.deltaTime;

                // 限制最大速度
                if (rb2d.linearVelocity.magnitude > maxSpeed.Value)
                {
                    rb2d.linearVelocity = rb2d.linearVelocity.normalized * maxSpeed.Value;
                }
            }
            else
            {
                // 直接移动 Transform
                float moveSpeed = Mathf.Min(acceleration.Value * elapsedTime, maxSpeed.Value);
                selfTransform.position += (Vector3)(direction * moveSpeed * Time.deltaTime);
            }
        }

        public override void OnExit()
        {
            // 清理
            rb2d = null;
            selfTransform = null;
            resolvedTarget = null;
        }

        /// <summary>
        /// 解析追踪目标
        /// </summary>
        private Transform? ResolveTarget()
        {
            // 优先使用直接设置的 Transform
            if (targetTransform != null)
            {
                return targetTransform;
            }

            // 其次使用 FsmGameObject
            if (targetGameObject != null && targetGameObject.Value != null)
            {
                return targetGameObject.Value.transform;
            }

            // 默认追踪玩家
            var heroController = Object.FindFirstObjectByType<HeroController>();
            return heroController?.transform;
        }
    }
}
