using UnityEngine;
using HutongGames.PlayMaker;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 延迟启用/禁用 Behaviour 组件（类似于 ActivateGameObjectDelay）
    /// 用于精确控制组件的 enabled 状态
    /// </summary>
    [ActionCategory("AnySilkBoss")]
    [HutongGames.PlayMaker.Tooltip("延迟启用或禁用 Behaviour 组件。")]
    public class EnableBehaviourDelay : FsmStateAction
    {
        [RequiredField]
        [HutongGames.PlayMaker.Tooltip("要控制的 Behaviour 组件")]
        public FsmObject? behaviour;

        [RequiredField]
        [HutongGames.PlayMaker.Tooltip("启用或禁用组件")]
        public FsmBool? enable;

        [HutongGames.PlayMaker.Tooltip("延迟时间（秒）")]
        public FsmFloat? delay;

        [HutongGames.PlayMaker.Tooltip("退出状态时重置组件状态")]
        public bool resetOnExit;

        private float timer;
        private Behaviour? targetBehaviour;
        private bool initialEnabledState;

        public override void Reset()
        {
            behaviour = null;
            enable = null;
            delay = null;
            resetOnExit = false;
            timer = 0f;
        }

        public override void OnEnter()
        {
            targetBehaviour = behaviour?.Value as Behaviour;
            
            if (targetBehaviour == null)
            {
                LogError("Behaviour 组件为 null 或类型不正确");
                Finish();
                return;
            }

            // 保存初始状态
            initialEnabledState = targetBehaviour.enabled;

            // 如果延迟为 0 或负数，立即执行
            if (delay == null || delay.Value <= 0f)
            {
                DoEnableBehaviour();
                return;
            }

            timer = delay.Value;
        }

        public override void OnUpdate()
        {
            if (timer > 0f)
            {
                timer -= Time.deltaTime;
                return;
            }

            DoEnableBehaviour();
        }

        public override void OnExit()
        {
            if (targetBehaviour == null || !resetOnExit)
            {
                return;
            }

            // 重置为初始状态
            targetBehaviour.enabled = initialEnabledState;
        }

        private void DoEnableBehaviour()
        {
            if (targetBehaviour == null || enable == null)
            {
                Finish();
                return;
            }

            targetBehaviour.enabled = enable.Value;
            Finish();
        }
    }
}

