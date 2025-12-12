using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    internal class RubbleFieldBehavior : MonoBehaviour
    {
        private PlayMakerFSM _fsm;
        private string _rockName1;
        private string _rockName2;
        private float _targetSpeed1 = -25f;
        private float _targetSpeed2 = -15f;
        private string objName;
        private void Start()
        {
            StartCoroutine(ModifyFSM());

        }

        private IEnumerator ModifyFSM()
        {
            // 等待一帧确保 FSM 初始化
            yield return null;

            _fsm = GetComponent<PlayMakerFSM>();
            if (_fsm == null)
            {
                Debug.LogWarning("RubbleFieldBehavior: PlayMakerFSM not found.");
                yield break;
            }
            AdjustScaleAndPosition();
            ModifySetAttackState();
            ModifyAnticState();
            ModifyRockSpawnState();
            _fsm.Fsm.InitData();
            _fsm.FsmVariables.Init();
        }
        private void ModifyIdleState()
        {

        }

        /// <summary>
        /// 根据 Rubble Field 类型调整 X 轴缩放和位置
        /// </summary>
        private void AdjustScaleAndPosition()
        {
            objName = gameObject.name;
            Vector3 scale = transform.localScale;
            Vector3 pos = transform.localPosition;

            if (objName.Contains("Rubble Field M"))
            {
                scale.x = 1.75f;
            }
            else if (objName.Contains("Rubble Field L"))
            {
                scale.x = 2f;
            }
            else if (objName.Contains("Rubble Field R"))
            {
                scale.x = 2f;
            }

            transform.localScale = scale;
            transform.localPosition = pos;
        }
        private void ModifySetAttackState()
        {
            var state = _fsm.FsmStates.FirstOrDefault(s => s.Name == "Set Attack");
            if (state != null)
            {
                var action = state.Actions.OfType<SetFloatValue>().FirstOrDefault();
                if (action != null)
                {
                    action.floatValue = 3.5f;
                }
            }
        }
        private void ModifyAnticState()
        {
            var state = _fsm.FsmStates.FirstOrDefault(s => s.Name == "Antic");
            if (state != null)
            {
                var action = state.Actions.OfType<PlayParticleEmitterChildren>().FirstOrDefault();
                if (action != null)
                {
                    action.Enabled = false;
                }
            }
        }
        private void ModifyRockSpawnState()
        {
            var state = _fsm.FsmStates.FirstOrDefault(s => s.Name == "Rock Spawn");
            if (state != null)
            {
                var actions = state.Actions.ToList();
                var spawnActions = actions.OfType<SpawnObjectFromGlobalPoolOverTimeV2>().ToArray();

                // 第一个 SpawnObjectFromGlobalPoolOverTimeV2
                if (spawnActions.Length > 0 && spawnActions[0] != null)
                {
                    spawnActions[0].frequency = 0.2f;
                    spawnActions[0].scaleMin = 0.5f;
                    spawnActions[0].scaleMax = 1.5f;

                    // 扩大生成范围 50%
                    if (objName.Contains("Rubble Field M") && spawnActions[0].originVariationX != null)
                        spawnActions[0].originVariationX.Value *= 1.5f;
                    else if (objName.Contains("Rubble Field L") && spawnActions[0].originVariationX != null)
                        spawnActions[0].originVariationX.Value *= 2f;
                    else if (objName.Contains("Rubble Field R") && spawnActions[0].originVariationX != null)
                        spawnActions[0].originVariationX.Value *= 2f;
                    // 记录预制体名称用于后续检测
                    if (spawnActions[0].gameObject.Value != null)
                    {
                        _rockName1 = spawnActions[0].gameObject.Value.name;
                    }
                }

                // 第二个 SpawnObjectFromGlobalPoolOverTimeV2
                if (spawnActions.Length > 1 && spawnActions[1] != null)
                {
                    spawnActions[1].frequency = 1f;
                    spawnActions[1].scaleMin = 0.6f;
                    spawnActions[1].scaleMax = 1f;

                    // 记录预制体名称用于后续检测
                    if (spawnActions[1].gameObject.Value != null)
                    {
                        _rockName2 = spawnActions[1].gameObject.Value.name;
                    }
                }
                var fadeNestedFadeGroupAction = actions.FirstOrDefault(a => a is FadeNestedFadeGroup);
                if (fadeNestedFadeGroupAction != null)
                {
                    actions.Remove(fadeNestedFadeGroupAction);
                }
                // 设置 ActivateGameObjectDelay 的 activate 值为 False
                var activateAction = state.Actions.OfType<ActivateGameObjectDelay>().FirstOrDefault();
                if (activateAction != null)
                {
                    activateAction.activate = false;
                }
                // 删除 AnimatePositionTo
                var animateAction = actions.FirstOrDefault(a => a is AnimatePositionTo);
                if (animateAction != null)
                {
                    actions.Remove(animateAction);
                }
                state.Actions = actions.ToArray();
            }
        }

    }
}
