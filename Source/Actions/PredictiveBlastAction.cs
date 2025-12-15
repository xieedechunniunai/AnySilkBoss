using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 预判爆炸 Action - 根据玩家当前位置和速度预测未来位置，在该位置生成爆炸
    /// 
    /// 预测公式：predictedPos = heroPos + heroVelocity * predictionTime
    /// 
    /// 使用场景：
    /// - 黑屏密集爆炸中的预判攻击
    /// - 智能追踪爆炸
    /// </summary>
    public class PredictiveBlastAction : FsmStateAction
    {
        #region 参数配置
        [HutongGames.PlayMaker.Tooltip("预测时间（秒），玩家在这个时间后会到达的位置")]
        public FsmFloat predictionTime;

        [HutongGames.PlayMaker.Tooltip("预测位置的随机偏移范围")]
        public FsmFloat positionRandomOffset;

        [HutongGames.PlayMaker.Tooltip("是否立即生成爆炸（false 则只计算位置，输出到 predictedPosition）")]
        public FsmBool spawnBlastImmediately;

        [HutongGames.PlayMaker.Tooltip("输出：预测的目标位置")]
        public FsmVector3 predictedPosition;

        [HutongGames.PlayMaker.Tooltip("爆炸生成后的回调事件")]
        public FsmEvent? onBlastSpawned;

        [HutongGames.PlayMaker.Tooltip("场地边界限制 - 最小 X")]
        public FsmFloat boundaryMinX;

        [HutongGames.PlayMaker.Tooltip("场地边界限制 - 最大 X")]
        public FsmFloat boundaryMaxX;

        [HutongGames.PlayMaker.Tooltip("场地边界限制 - 最小 Y")]
        public FsmFloat boundaryMinY;

        [HutongGames.PlayMaker.Tooltip("场地边界限制 - 最大 Y")]
        public FsmFloat boundaryMaxY;
        #endregion

        #region 管理器引用
        private FWBlastManager? blastManager;
        #endregion

        public override void Reset()
        {
            predictionTime = 0.5f;
            positionRandomOffset = 1f;
            spawnBlastImmediately = true;
            predictedPosition = Vector3.zero;
            onBlastSpawned = null;
            
            // 默认场地边界（根据实际场地调整）
            boundaryMinX = 25f;
            boundaryMaxX = 50f;
            boundaryMinY = 5f;
            boundaryMaxY = 25f;
        }

        public override void OnEnter()
        {
            // 获取玩家
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log("PredictiveBlastAction: 未找到玩家");
                Finish();
                return;
            }

            // 计算预测位置
            Vector3 predictedPos = CalculatePredictedPosition(hero);
            predictedPosition.Value = predictedPos;

            // 如果需要立即生成爆炸
            if (spawnBlastImmediately.Value)
            {
                GetManagerReferences();
                if (blastManager != null)
                {
                    blastManager.SpawnBombBlast(predictedPos);
                    
                    if (onBlastSpawned != null)
                    {
                        Fsm.Event(onBlastSpawned);
                    }
                }
            }

            Finish();
        }

        /// <summary>
        /// 计算预测位置
        /// </summary>
        private Vector3 CalculatePredictedPosition(HeroController hero)
        {
            Vector3 heroPos = hero.transform.position;
            Vector2 heroVelocity = Vector2.zero;

            // 获取玩家速度
            var heroRb = hero.GetComponent<Rigidbody2D>();
            if (heroRb != null)
            {
                heroVelocity = heroRb.linearVelocity;
            }

            // 预测位置
            Vector3 predicted = heroPos + new Vector3(heroVelocity.x, heroVelocity.y, 0) * predictionTime.Value;

            // 添加随机偏移
            if (positionRandomOffset.Value > 0)
            {
                predicted.x += Random.Range(-positionRandomOffset.Value, positionRandomOffset.Value);
                predicted.y += Random.Range(-positionRandomOffset.Value, positionRandomOffset.Value);
            }

            // 限制在场地边界内
            predicted.x = Mathf.Clamp(predicted.x, boundaryMinX.Value, boundaryMaxX.Value);
            predicted.y = Mathf.Clamp(predicted.y, boundaryMinY.Value, boundaryMaxY.Value);

            return predicted;
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
            }
        }

        /// <summary>
        /// 静态方法：直接计算预测位置（供其他代码调用）
        /// </summary>
        public static Vector3 CalculatePrediction(float predictionTime, float randomOffset = 0f)
        {
            var hero = HeroController.instance;
            if (hero == null) return Vector3.zero;

            Vector3 heroPos = hero.transform.position;
            Vector2 heroVelocity = Vector2.zero;

            var heroRb = hero.GetComponent<Rigidbody2D>();
            if (heroRb != null)
            {
                heroVelocity = heroRb.linearVelocity;
            }

            Vector3 predicted = heroPos + new Vector3(heroVelocity.x, heroVelocity.y, 0) * predictionTime;

            if (randomOffset > 0)
            {
                predicted.x += Random.Range(-randomOffset, randomOffset);
                predicted.y += Random.Range(-randomOffset, randomOffset);
            }

            return predicted;
        }
    }
}
