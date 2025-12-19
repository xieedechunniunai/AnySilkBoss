using UnityEngine;
using HutongGames.PlayMaker;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 通用跟随目标 Action - 让物体跟随另一个物体的位置和角度
    /// 
    /// 使用场景：
    /// - 大丝球跟随Boss胸前
    /// - 武器/特效跟随角色
    /// - 子物体跟随父物体（带偏移）
    /// </summary>
    public class FollowTargetAction : FsmStateAction
    {
        #region 目标配置
        [HutongGames.PlayMaker.Tooltip("跟随目标的 GameObject")]
        public FsmGameObject? targetGameObject;

        [HutongGames.PlayMaker.Tooltip("跟随目标的 Transform（直接引用，优先级高于 targetGameObject）")]
        public Transform? targetTransform;

        [HutongGames.PlayMaker.Tooltip("位置偏移（相对于目标）")]
        public FsmVector3? positionOffset;

        [HutongGames.PlayMaker.Tooltip("是否使用局部坐标系计算偏移（true=偏移量随目标旋转，false=世界坐标偏移）")]
        public FsmBool? useLocalOffset;
        #endregion

        #region 位置跟随配置
        [HutongGames.PlayMaker.Tooltip("是否跟随 X 轴")]
        public FsmBool? followX;

        [HutongGames.PlayMaker.Tooltip("是否跟随 Y 轴")]
        public FsmBool? followY;

        [HutongGames.PlayMaker.Tooltip("是否跟随 Z 轴")]
        public FsmBool? followZ;

        [HutongGames.PlayMaker.Tooltip("位置平滑速度（0=立即跟随，>0=平滑插值）")]
        public FsmFloat? positionSmoothSpeed;
        #endregion

        #region 角度跟随配置
        [HutongGames.PlayMaker.Tooltip("是否跟随角度（Z轴旋转）")]
        public FsmBool? followRotation;

        [HutongGames.PlayMaker.Tooltip("角度偏移（相对于目标）")]
        public FsmFloat? rotationOffset;

        [HutongGames.PlayMaker.Tooltip("角度平滑速度（0=立即跟随，>0=平滑插值）")]
        public FsmFloat? rotationSmoothSpeed;
        #endregion

        #region 更新模式配置
        [HutongGames.PlayMaker.Tooltip("是否在 Update 中更新（默认true）")]
        public FsmBool? updateInUpdate;

        [HutongGames.PlayMaker.Tooltip("是否在 LateUpdate 中更新（用于跟随动画后的位置）")]
        public FsmBool? updateInLateUpdate;

        [HutongGames.PlayMaker.Tooltip("是否强制直接设置位置（忽略平滑，每帧直接赋值）")]
        public FsmBool? forceDirectSet;
        #endregion

        #region 运行时变量
        private Transform? selfTransform;
        private Transform? resolvedTarget;
        #endregion

        public override void Reset()
        {
            // 目标配置
            targetGameObject = null;
            targetTransform = null;
            positionOffset = Vector3.zero;
            useLocalOffset = false;
            // 位置跟随
            followX = true;
            followY = true;
            followZ = false;
            positionSmoothSpeed = 0f;
            // 角度跟随
            followRotation = false;
            rotationOffset = 0f;
            rotationSmoothSpeed = 0f;
            // 更新模式
            updateInUpdate = true;
            updateInLateUpdate = false;
            forceDirectSet = false;
        }

        public override void OnEnter()
        {
            if (Owner == null)
            {
                Log("FollowTargetAction: Owner 为 null");
                Finish();
                return;
            }

            selfTransform = Owner.transform;

            // 解析目标
            resolvedTarget = ResolveTarget();
            if (resolvedTarget == null)
            {
                Log("FollowTargetAction: 未找到跟随目标");
                Finish();
                return;
            }

            // 立即执行一次跟随
            DoFollow();
        }

        public override void OnUpdate()
        {
            if (updateInUpdate?.Value != false)
            {
                DoFollow();
            }
        }

        public override void OnLateUpdate()
        {
            if (updateInLateUpdate?.Value == true)
            {
                DoFollow();
            }
        }

        public override void OnExit()
        {
            selfTransform = null;
            resolvedTarget = null;
        }

        private void DoFollow()
        {
            if (selfTransform == null || resolvedTarget == null)
            {
                return;
            }

            // 计算目标位置
            Vector3 targetPos = resolvedTarget.position;
            Vector3 offset = positionOffset?.Value ?? Vector3.zero;

            // 如果使用局部坐标系，将偏移量转换为世界坐标
            if (useLocalOffset?.Value == true)
            {
                offset = resolvedTarget.TransformDirection(offset);
            }

            targetPos += offset;

            // 应用位置跟随
            Vector3 newPosition = selfTransform.position;
            bool forceDirect = forceDirectSet?.Value == true;
            float smoothSpeed = forceDirect ? 0f : (positionSmoothSpeed?.Value ?? 0f);

            if (followX?.Value == true)
            {
                newPosition.x = (smoothSpeed > 0 && !forceDirect)
                    ? Mathf.Lerp(newPosition.x, targetPos.x, smoothSpeed * Time.deltaTime)
                    : targetPos.x;
            }

            if (followY?.Value == true)
            {
                newPosition.y = (smoothSpeed > 0 && !forceDirect)
                    ? Mathf.Lerp(newPosition.y, targetPos.y, smoothSpeed * Time.deltaTime)
                    : targetPos.y;
            }

            if (followZ?.Value == true)
            {
                newPosition.z = (smoothSpeed > 0 && !forceDirect)
                    ? Mathf.Lerp(newPosition.z, targetPos.z, smoothSpeed * Time.deltaTime)
                    : targetPos.z;
            }

            selfTransform.position = newPosition;

            // 应用角度跟随
            if (followRotation?.Value == true)
            {
                float targetRotZ = resolvedTarget.eulerAngles.z + (rotationOffset?.Value ?? 0f);
                float rotSmoothSpeed = forceDirect ? 0f : (rotationSmoothSpeed?.Value ?? 0f);

                if (rotSmoothSpeed > 0 && !forceDirect)
                {
                    float currentRotZ = selfTransform.eulerAngles.z;
                    float newRotZ = Mathf.LerpAngle(currentRotZ, targetRotZ, rotSmoothSpeed * Time.deltaTime);
                    selfTransform.rotation = Quaternion.Euler(0, 0, newRotZ);
                }
                else
                {
                    selfTransform.rotation = Quaternion.Euler(0, 0, targetRotZ);
                }
            }
        }

        /// <summary>
        /// 解析跟随目标
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

            return null;
        }

        /// <summary>
        /// 运行时更新目标（供外部调用）
        /// </summary>
        public void SetTarget(Transform target)
        {
            targetTransform = target;
            resolvedTarget = target;
        }

        /// <summary>
        /// 运行时更新偏移（供外部调用）
        /// </summary>
        public void SetOffset(Vector3 offset)
        {
            if (positionOffset != null)
            {
                positionOffset.Value = offset;
            }
            else
            {
                positionOffset = new FsmVector3 { Value = offset };
            }
        }
    }
}
