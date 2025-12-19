using UnityEngine;
using HutongGames.PlayMaker;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 软锁定/追踪 Action - 让 Owner 在指定轴上跟随目标，支持边界限制和旋转锁定
    /// 原 MemoryFingerBladeTrackAction / FingerBladeTrackAction 的通用化版本
    /// </summary>
    public class SoftLockTrackAction : FsmStateAction
    {
        #region 目标配置
        /// <summary>目标 GameObject（可选，若不设置则默认追踪玩家）</summary>
        public FsmGameObject? targetGameObject;
        /// <summary>目标 Transform（优先级高于 targetGameObject）</summary>
        public Transform? targetTransform;
        #endregion

        #region 偏移配置
        /// <summary>相对于目标的额外位置偏移</summary>
        public FsmVector3? positionOffset;
        /// <summary>true=进入时记录自身相对目标的偏移并保持；false=使用 positionOffset 作为固定偏移</summary>
        public FsmBool? maintainInitialOffset;
        #endregion

        #region 轴跟随配置
        /// <summary>是否在 X 轴上跟随目标</summary>
        public FsmBool? followX;
        /// <summary>是否在 Y 轴上跟随目标</summary>
        public FsmBool? followY;
        /// <summary>是否在 Z 轴上跟随目标</summary>
        public FsmBool? followZ;
        #endregion

        #region 固定坐标（当不跟随对应轴时使用）
        /// <summary>固定 X 坐标（当 followX=false 且设置此值时使用）</summary>
        public FsmFloat? targetX;
        /// <summary>固定 Y 坐标（当 followY=false 且设置此值时使用）</summary>
        public FsmFloat? targetY;
        /// <summary>固定 Z 坐标（当 followZ=false 且设置此值时使用）</summary>
        public FsmFloat? targetZ;
        #endregion

        #region 位置插值
        /// <summary>位置插值速度（0=瞬移，>0=平滑跟随）</summary>
        public FsmFloat? positionLerpSpeed;
        #endregion

        #region 边界限制（生效范围）
        /// <summary>是否启用 X 轴最小值限制</summary>
        public FsmBool? useMinX;
        /// <summary>X 轴最小值</summary>
        public FsmFloat? minX;
        /// <summary>是否启用 X 轴最大值限制</summary>
        public FsmBool? useMaxX;
        /// <summary>X 轴最大值</summary>
        public FsmFloat? maxX;

        /// <summary>是否启用 Y 轴最小值限制</summary>
        public FsmBool? useMinY;
        /// <summary>Y 轴最小值（如地面高度）</summary>
        public FsmFloat? minY;
        /// <summary>是否启用 Y 轴最大值限制</summary>
        public FsmBool? useMaxY;
        /// <summary>Y 轴最大值</summary>
        public FsmFloat? maxY;
        #endregion

        #region 旋转配置
        /// <summary>固定 Z 轴旋转角度（度），可绑定 FSM 变量实时更新</summary>
        public FsmFloat? rotationZ;
        /// <summary>旋转插值速度（0=瞬转，>0=平滑旋转）</summary>
        public FsmFloat? rotationLerpSpeed;
        #endregion

        #region 超时配置
        /// <summary>最大持续时间（秒，0=无限）</summary>
        public FsmFloat? maxDuration;
        /// <summary>超时时触发的事件</summary>
        public FsmEvent? onTimeout;
        /// <summary>超时后是否结束该 Action</summary>
        public FsmBool? finishAfterTimeout;
        #endregion

        #region 速度处理
        /// <summary>进入状态时是否清零 Rigidbody2D 速度</summary>
        public FsmBool? zeroVelocityOnEnter;
        /// <summary>退出状态时是否清零 Rigidbody2D 速度</summary>
        public FsmBool? zeroVelocityOnExit;
        #endregion

        private Transform? _selfTransform;
        private Transform? _resolvedTarget;

        private Vector3 _fixedPosition;
        private Vector3 _runtimeOffset;
        private float _elapsed;

        public override void Reset()
        {
            targetGameObject = null;
            targetTransform = null;

            positionOffset = Vector3.zero;
            maintainInitialOffset = true;

            followX = false;
            followY = false;
            followZ = false;

            targetX = null;
            targetY = null;
            targetZ = null;

            positionLerpSpeed = 15f;

            useMinX = false;
            minX = 0f;
            useMaxX = false;
            maxX = 0f;

            useMinY = false;
            minY = 0f;
            useMaxY = false;
            maxY = 0f;

            rotationZ = null;
            rotationLerpSpeed = 20f;

            maxDuration = 0f;
            onTimeout = null;
            finishAfterTimeout = true;

            zeroVelocityOnEnter = true;
            zeroVelocityOnExit = true;
        }

        public override void OnEnter()
        {
            if (Owner == null)
            {
                Finish();
                return;
            }

            _selfTransform = Owner.transform;
            _resolvedTarget = ResolveTarget();
            if (_resolvedTarget == null)
            {
                Finish();
                return;
            }

            _elapsed = 0f;
            _fixedPosition = _selfTransform.position;

            Vector3 configuredOffset = positionOffset?.Value ?? Vector3.zero;
            if (maintainInitialOffset?.Value != false)
            {
                _runtimeOffset = _selfTransform.position - _resolvedTarget.position;
            }
            else
            {
                _runtimeOffset = Vector3.zero;
            }

            _runtimeOffset += configuredOffset;

            if (zeroVelocityOnEnter?.Value == true)
            {
                ZeroVelocity();
            }

            ApplyRotation(Time.deltaTime);
        }

        public override void OnUpdate()
        {
            if (_selfTransform == null || _resolvedTarget == null)
            {
                Finish();
                return;
            }

            float dt = Time.deltaTime;
            _elapsed += dt;

            if (maxDuration != null && maxDuration.Value > 0f && _elapsed >= maxDuration.Value)
            {
                if (onTimeout != null)
                {
                    Fsm.Event(onTimeout);
                }

                if (finishAfterTimeout?.Value != false)
                {
                    Finish();
                }
                return;
            }

            Vector3 current = _selfTransform.position;
            Vector3 dest = current;

            if (followX?.Value == true)
            {
                dest.x = _resolvedTarget.position.x + _runtimeOffset.x;
            }
            else if (targetX != null)
            {
                dest.x = targetX.Value;
            }
            else
            {
                dest.x = _fixedPosition.x;
            }

            if (followY?.Value == true)
            {
                dest.y = _resolvedTarget.position.y + _runtimeOffset.y;
            }
            else if (targetY != null)
            {
                dest.y = targetY.Value;
            }
            else
            {
                dest.y = _fixedPosition.y;
            }

            if (followZ?.Value == true)
            {
                dest.z = _resolvedTarget.position.z + _runtimeOffset.z;
            }
            else if (targetZ != null)
            {
                dest.z = targetZ.Value;
            }
            else
            {
                dest.z = _fixedPosition.z;
            }

            if (useMinX?.Value == true && minX != null)
            {
                dest.x = Mathf.Max(dest.x, minX.Value);
            }
            if (useMaxX?.Value == true && maxX != null)
            {
                dest.x = Mathf.Min(dest.x, maxX.Value);
            }
            if (useMinY?.Value == true && minY != null)
            {
                dest.y = Mathf.Max(dest.y, minY.Value);
            }
            if (useMaxY?.Value == true && maxY != null)
            {
                dest.y = Mathf.Min(dest.y, maxY.Value);
            }

            float lerpSpeed = positionLerpSpeed?.Value ?? 0f;
            if (lerpSpeed > 0f)
            {
                Vector3 newPos = Vector3.Lerp(current, dest, dt * lerpSpeed);
                _selfTransform.position = newPos;
            }
            else
            {
                _selfTransform.position = dest;
            }

            ApplyRotation(dt);
        }

        public override void OnExit()
        {
            if (zeroVelocityOnExit?.Value == true)
            {
                ZeroVelocity();
            }

            _selfTransform = null;
            _resolvedTarget = null;
        }

        private Transform? ResolveTarget()
        {
            if (targetTransform != null)
            {
                return targetTransform;
            }

            if (targetGameObject != null && targetGameObject.Value != null)
            {
                return targetGameObject.Value.transform;
            }

            var heroController = Object.FindFirstObjectByType<HeroController>();
            return heroController?.transform;
        }

        private void ApplyRotation(float dt)
        {
            if (_selfTransform == null)
            {
                return;
            }

            if (rotationZ == null)
            {
                return;
            }

            Quaternion targetRot = Quaternion.Euler(0f, 0f, rotationZ.Value);
            float lerpSpeed = rotationLerpSpeed?.Value ?? 0f;
            _selfTransform.rotation = (lerpSpeed > 0f)
                ? Quaternion.Lerp(_selfTransform.rotation, targetRot, dt * lerpSpeed)
                : targetRot;
        }

        private void ZeroVelocity()
        {
            if (_selfTransform == null)
            {
                return;
            }

            var rb2d = _selfTransform.GetComponent<Rigidbody2D>();
            if (rb2d != null)
            {
                rb2d.linearVelocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
            }
        }
    }
}
