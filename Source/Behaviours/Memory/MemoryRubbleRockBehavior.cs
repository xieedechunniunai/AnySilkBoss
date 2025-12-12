using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 挂载到 Rubble Field 生成的石头上，修改其 Control FSM 中 Drop 状态的 AccelerateToY 的 targetSpeed
    /// </summary>
    internal class MemoryRubbleRockBehavior : MonoBehaviour
    {
        /// <summary>
        /// AccelerateToY 的目标速度
        /// </summary>
        public float TargetSpeed = -20f;

        private void OnEnable()
        {
            ModifyDropState();
        }

        private void ModifyDropState()
        {
            // 查找名为 "Control" 的 PlayMakerFSM
            var controlFsm = GetComponents<PlayMakerFSM>()
                .FirstOrDefault(fsm => fsm.FsmName == "Control");
            
            if (controlFsm == null) return;

            // 查找 "Drop" 状态
            var dropState = controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Drop");
            if (dropState == null) return;

            // 查找第一个 AccelerateToY 行为并修改 targetSpeed
            var accelerateAction = dropState.Actions.OfType<AccelerateToY>().FirstOrDefault();
            if (accelerateAction != null)
            {
                accelerateAction.targetSpeed = TargetSpeed;
            }
        }
    }
}
