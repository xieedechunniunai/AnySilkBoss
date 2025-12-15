using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Managers;
using System.Collections;
using System.Collections.Generic;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 汇聚爆炸 Action - 生成多个爆炸并使其向 Boss 位置汇聚
    /// 
    /// 功能：
    /// - 在外围生成多个爆炸预警点
    /// - 爆炸效果向 Boss 位置移动/汇聚
    /// - 最终在 Boss 位置触发一个大爆炸
    /// 
    /// 使用场景：
    /// - 汇聚爆炸招式（多个原罪者爆炸汇聚到 Boss 自身）
    /// </summary>
    public class ConvergeBlastAction : FsmStateAction
    {
        #region 参数配置
        [HutongGames.PlayMaker.Tooltip("Boss GameObject（汇聚目标）")]
        public FsmGameObject bossObject;

        [HutongGames.PlayMaker.Tooltip("爆炸数量")]
        public FsmInt blastCount;

        [HutongGames.PlayMaker.Tooltip("生成半径（从 Boss 位置的距离）")]
        public FsmFloat spawnRadius;

        [HutongGames.PlayMaker.Tooltip("汇聚速度（单位/秒）")]
        public FsmFloat convergeSpeed;

        [HutongGames.PlayMaker.Tooltip("每个爆炸的生成间隔（秒）")]
        public FsmFloat spawnInterval;

        [HutongGames.PlayMaker.Tooltip("汇聚完成后的延迟（秒），然后生成最终爆炸")]
        public FsmFloat finalBlastDelay;

        [HutongGames.PlayMaker.Tooltip("是否在汇聚完成后生成最终大爆炸")]
        public FsmBool spawnFinalBlast;

        [HutongGames.PlayMaker.Tooltip("最终爆炸的缩放倍数")]
        public FsmFloat finalBlastScale;

        [HutongGames.PlayMaker.Tooltip("汇聚完成后触发的事件")]
        public FsmEvent? onConvergeComplete;

        [HutongGames.PlayMaker.Tooltip("所有爆炸结束后触发的事件")]
        public FsmEvent? onAllComplete;
        #endregion

        #region 运行时变量
        private FWBlastManager? blastManager;
        private List<GameObject> activeBlasts = new List<GameObject>();
        private Vector3 bossPosition;
        private int convergedCount;
        private bool isConverging;
        #endregion

        public override void Reset()
        {
            bossObject = null;
            blastCount = 6;
            spawnRadius = 15f;
            convergeSpeed = 10f;
            spawnInterval = 0.2f;
            finalBlastDelay = 0.5f;
            spawnFinalBlast = true;
            finalBlastScale = 2f;
            onConvergeComplete = null;
            onAllComplete = null;
        }

        public override void OnEnter()
        {
            // 获取管理器
            GetManagerReferences();
            if (blastManager == null)
            {
                Log("ConvergeBlastAction: 未找到 FWBlastManager");
                Finish();
                return;
            }

            // 获取 Boss 位置
            if (bossObject.Value == null)
            {
                Log("ConvergeBlastAction: Boss 对象为空");
                Finish();
                return;
            }

            bossPosition = bossObject.Value.transform.position;
            convergedCount = 0;
            isConverging = true;
            activeBlasts.Clear();

            // 开始生成爆炸
            StartCoroutine(SpawnAndConvergeBlasts());
        }

        public override void OnUpdate()
        {
            if (!isConverging) return;

            // 更新 Boss 位置（Boss 可能在移动）
            if (bossObject.Value != null)
            {
                bossPosition = bossObject.Value.transform.position;
            }

            // 更新所有活动爆炸的位置（向 Boss 移动）
            for (int i = activeBlasts.Count - 1; i >= 0; i--)
            {
                var blast = activeBlasts[i];
                if (blast == null)
                {
                    activeBlasts.RemoveAt(i);
                    continue;
                }

                // 移动爆炸向 Boss
                Vector3 direction = (bossPosition - blast.transform.position).normalized;
                blast.transform.position += direction * convergeSpeed.Value * Time.deltaTime;

                // 检查是否到达 Boss
                float distance = Vector3.Distance(blast.transform.position, bossPosition);
                if (distance < 1f)
                {
                    // 到达，销毁或回收
                    activeBlasts.RemoveAt(i);
                    convergedCount++;

                    // 可以在这里播放到达效果
                }
            }
        }

        public override void OnExit()
        {
            isConverging = false;
            activeBlasts.Clear();
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
        /// 生成并汇聚爆炸的协程
        /// </summary>
        private IEnumerator SpawnAndConvergeBlasts()
        {
            int count = blastCount.Value;
            float angleStep = 360f / count;
            float startAngle = Random.Range(0f, 360f);

            // 生成所有爆炸
            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + i * angleStep;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 计算生成位置
                Vector3 spawnPos = bossPosition + new Vector3(direction.x, direction.y, 0) * spawnRadius.Value;

                // 生成爆炸
                // 注意：这里可能需要生成一个可移动的爆炸效果，而不是原版的固定位置爆炸
                // 暂时使用原版爆炸，后续可以改为自定义的可移动爆炸
                var blast = blastManager?.SpawnBombBlast(spawnPos);
                
                // TODO: 实际实现中，需要让爆炸效果可以移动
                // 这里的逻辑需要根据 FWBlastManager 的实际实现来调整
                // 可能需要：
                // 1. 生成一个虚拟的追踪物体，带有爆炸视觉效果
                // 2. 物体移动到 Boss 位置后触发实际爆炸

                if (spawnInterval.Value > 0)
                {
                    yield return new WaitForSeconds(spawnInterval.Value);
                }
            }

            // 等待所有爆炸汇聚完成
            float maxWaitTime = (spawnRadius.Value / convergeSpeed.Value) + 2f;
            float waitedTime = 0f;

            while (activeBlasts.Count > 0 && waitedTime < maxWaitTime)
            {
                yield return null;
                waitedTime += Time.deltaTime;
            }

            // 触发汇聚完成事件
            if (onConvergeComplete != null)
            {
                Fsm.Event(onConvergeComplete);
            }

            // 生成最终大爆炸
            if (spawnFinalBlast.Value)
            {
                yield return new WaitForSeconds(finalBlastDelay.Value);
                
                // 在 Boss 位置生成最终爆炸
                // TODO: 需要支持缩放的爆炸
                blastManager?.SpawnBombBlast(bossPosition);
            }

            // 触发全部完成事件
            if (onAllComplete != null)
            {
                Fsm.Event(onAllComplete);
            }

            isConverging = false;
            Finish();
        }
    }
}
