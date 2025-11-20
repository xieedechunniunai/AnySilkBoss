using UnityEngine;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// 大丝球碰撞箱 - 处理吸收小丝球的碰撞逻辑
    /// 位于Z=0的实际碰撞层，负责检测和吸收小丝球
    /// </summary>
    internal class BigSilkBallCollisionBox : MonoBehaviour
    {
        public BigSilkBallBehavior? parentBehavior;
        private CircleCollider2D? circleCollider;
        private Rigidbody2D? rb;

        private void Awake()
        {
            // 添加刚体（Kinematic类型，不受物理影响但可以触发碰撞事件）
            rb = gameObject.AddComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;  // Kinematic：不受重力和力影响
                rb.useFullKinematicContacts = true;       // 允许与动态刚体产生碰撞回调
                rb.gravityScale = 0f;                     // 无重力
                rb.constraints = RigidbodyConstraints2D.FreezeRotation; // 锁定旋转
                Log.Info($"碰撞箱Rigidbody2D已创建 - BodyType: {rb.bodyType}, useFullKinematicContacts: {rb.useFullKinematicContacts}");
            }

            // 添加圆形碰撞器
            circleCollider = gameObject.AddComponent<CircleCollider2D>();
            if (circleCollider != null)
            {
                circleCollider.radius = 5f;
                circleCollider.isTrigger = true;          // 设置为触发器
                Log.Info($"碰撞箱CircleCollider2D已创建 - 半径: {circleCollider.radius}, isTrigger: {circleCollider.isTrigger}");
            }
            
            Log.Info($"碰撞箱组件设置完成 - Layer: {LayerMask.LayerToName(gameObject.layer)}");
        }

        private void OnEnable()
        {
            Log.Info($"碰撞箱已激活 - 位置: {transform.position}, Layer: {LayerMask.LayerToName(gameObject.layer)}");
        }



        private void OnTriggerEnter2D(Collider2D other)
        {
            //Log.Info($"[大丝球碰撞箱] OnTriggerEnter2D被触发: {other.gameObject.name}, Layer: {LayerMask.LayerToName(other.gameObject.layer)}");
            
            if (parentBehavior == null)
            {
                Log.Warn($"[大丝球碰撞箱] parentBehavior为null，无法处理吸收");
                return;
            }

            // 检查是否是小丝球（先检查自身，再检查父物体）
            var silkBall = other.GetComponent<SilkBallBehavior>();
            if (silkBall == null)
            {
                // 如果是子物体（如Sprite Silk），尝试从父物体获取
                silkBall = other.GetComponentInParent<SilkBallBehavior>();
            }
            
            if (silkBall != null)
            {
                //Log.Info($"[大丝球碰撞箱] 检测到小丝球: {other.gameObject.name} (根物体: {silkBall.gameObject.name})");
                parentBehavior.OnAbsorbBall(silkBall);
            }
        }

        /// <summary>
        /// 被小丝球的碰撞转发器直接调用（双向检测）
        /// </summary>
        public void OnCollisionWithSilkBall(SilkBallBehavior silkBall)
        {
            if (parentBehavior == null)
            {
                Log.Warn($"[大丝球碰撞箱] parentBehavior为null，无法处理吸收");
                return;
            }

            Log.Info($"[大丝球碰撞箱] 小丝球主动报告碰撞: {silkBall.gameObject.name}");
            parentBehavior.OnAbsorbBall(silkBall);
        }

        /// <summary>
        /// 设置碰撞箱的缩放（跟随heart缩放）
        /// </summary>
        public void SetScale(float scale)
        {
            transform.localScale = Vector3.one * scale;
        }
    }
}

