using UnityEngine;
using HutongGames.PlayMaker;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 环绕目标 Action - 让 Owner 围绕指定目标做圆周运动
    /// 原 MemoryFingerBladeOrbitAction / FingerBladeOrbitAction 的通用化版本
    /// </summary>
    public class OrbitAroundTargetAction : FsmStateAction
    {
        #region 目标配置
        /// <summary>目标 GameObject（可选，若不设置则默认追踪玩家）</summary>
        public FsmGameObject? targetGameObject;
        /// <summary>目标 Transform（优先级高于 targetGameObject）</summary>
        public Transform? targetTransform;
        /// <summary>相对于目标的位置偏移</summary>
        public FsmVector3? targetPositionOffset;
        #endregion

        #region 环绕参数
        /// <summary>环绕半径（单位：Unity 单位）</summary>
        public FsmFloat orbitRadius = 7f;
        /// <summary>环绕速度（度/秒，正值顺时针，负值逆时针）</summary>
        public FsmFloat orbitSpeed = 200f;
        /// <summary>初始角度偏移（度），用于多个物体错开起始位置</summary>
        public FsmFloat orbitAngleOffset = 0f;
        #endregion

        #region 方向控制
        /// <summary>true=由 orbitSpeed 正负决定方向；false=由 clockwise 字段决定</summary>
        public FsmBool? useSpeedSignForDirection;
        /// <summary>当 useSpeedSignForDirection=false 时：true=顺时针，false=逆时针</summary>
        public FsmBool? clockwise;
        #endregion

        #region 跟随配置
        /// <summary>true=环绕中心跟随目标移动；false=锁定进入时的中心点</summary>
        public FsmBool? followTarget;
        #endregion

        #region 位置插值
        /// <summary>位置插值速度（0=瞬移，>0=平滑跟随）</summary>
        public FsmFloat? positionLerpSpeed;
        #endregion

        #region 旋转配置
        /// <summary>true=让物体始终朝向/背向圆心</summary>
        public FsmBool? rotateToFaceCenter;
        /// <summary>true=针尖朝外（背向圆心）；false=针尖朝内（朝向圆心）</summary>
        public FsmBool? pointOutward;
        /// <summary>基础旋转偏移角度（度）</summary>
        public FsmFloat? rotationOffset;
        /// <summary>方向性旋转偏移（度），根据旋转方向额外增减角度</summary>
        public FsmFloat? directionalRotationOffset;
        /// <summary>是否应用方向性旋转偏移</summary>
        public FsmBool? applyDirectionalRotationOffset;
        /// <summary>旋转插值速度（0=瞬转，>0=平滑旋转）</summary>
        public FsmFloat? rotationLerpSpeed;
        #endregion

        #region 超时配置
        /// <summary>最大持续时间（秒，0=无限）</summary>
        public FsmFloat? maxDuration;
        /// <summary>超时时触发的事件</summary>
        public FsmEvent? onTimeout;
        #endregion

        #region 完整旋转事件
        /// <summary>转满一圈（360°）时是否发送事件</summary>
        public FsmBool? sendEventOnFullRotation;
        /// <summary>转满一圈时触发的事件</summary>
        public FsmEvent? onFullRotation;
        /// <summary>转满一圈后是否结束该 Action</summary>
        public FsmBool? finishAfterFullRotation;
        #endregion

        #region 速度处理
        /// <summary>进入状态时是否清零 Rigidbody2D 速度</summary>
        public FsmBool? zeroVelocityOnEnter;
        /// <summary>退出状态时是否清零 Rigidbody2D 速度</summary>
        public FsmBool? zeroVelocityOnExit;
        #endregion

        private Transform? _selfTransform;
        private Transform? _resolvedTarget;
        private Vector3 _fixedTargetPosition;

        private float _elapsed;
        private float _currentAngle;
        private float _accumulatedAbsAngle;
        private float _lastAngle;
        private bool _initialized;

        public override void Reset()
        {
            targetGameObject = null;
            targetTransform = null;
            targetPositionOffset = Vector3.zero;

            orbitRadius = 7f;
            orbitSpeed = 200f;
            orbitAngleOffset = 0f;

            useSpeedSignForDirection = true;
            clockwise = true;
            followTarget = true;

            positionLerpSpeed = 30f;

            rotateToFaceCenter = true;
            pointOutward = false;
            rotationOffset = 180f;
            directionalRotationOffset = 10f;
            applyDirectionalRotationOffset = true;
            rotationLerpSpeed = 30f;

            maxDuration = 0f;
            onTimeout = null;

            sendEventOnFullRotation = false;
            onFullRotation = null;
            finishAfterFullRotation = false;

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
            _accumulatedAbsAngle = 0f;
            _initialized = false;

            _fixedTargetPosition = _resolvedTarget.position + (targetPositionOffset?.Value ?? Vector3.zero);

            _currentAngle = orbitAngleOffset.Value;
            _lastAngle = _currentAngle;

            if (zeroVelocityOnEnter?.Value == true)
            {
                ZeroVelocity();
            }
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
                Finish();
                return;
            }

            Vector3 center = (followTarget?.Value != false)
                ? (_resolvedTarget.position + (targetPositionOffset?.Value ?? Vector3.zero))
                : _fixedTargetPosition;

            float speed = orbitSpeed.Value;
            float directionSign;
            if (useSpeedSignForDirection?.Value != false)
            {
                directionSign = Mathf.Approximately(speed, 0f) ? 0f : Mathf.Sign(speed);
            }
            else
            {
                directionSign = (clockwise?.Value != false) ? 1f : -1f;
                speed = Mathf.Abs(speed);
            }

            _currentAngle += directionSign * Mathf.Abs(speed) * dt;

            if (!_initialized)
            {
                _lastAngle = _currentAngle;
                _initialized = true;
            }

            float deltaAngle = Mathf.DeltaAngle(_lastAngle, _currentAngle);
            _accumulatedAbsAngle += Mathf.Abs(deltaAngle);
            _lastAngle = _currentAngle;

            if (sendEventOnFullRotation?.Value == true && _accumulatedAbsAngle >= 360f)
            {
                _accumulatedAbsAngle -= 360f;
                if (onFullRotation != null)
                {
                    Fsm.Event(onFullRotation);
                }

                if (finishAfterFullRotation?.Value == true)
                {
                    Finish();
                    return;
                }
            }

            float radians = _currentAngle * Mathf.Deg2Rad;
            float radius = orbitRadius.Value;

            Vector3 circleOffset = new Vector3(
                Mathf.Cos(radians) * radius,
                Mathf.Sin(radians) * radius,
                0f
            );

            Vector3 targetPos = center + circleOffset;
            Vector3 currentPos = _selfTransform.position;

            float posLerp = positionLerpSpeed?.Value ?? 0f;
            Vector3 newPos = (posLerp > 0f)
                ? Vector3.Lerp(currentPos, targetPos, dt * posLerp)
                : targetPos;

            _selfTransform.position = new Vector3(newPos.x, newPos.y, currentPos.z);

            if (rotateToFaceCenter?.Value != false)
            {
                Vector3 faceDir = (pointOutward?.Value == true)
                    ? (_selfTransform.position - center)
                    : (center - _selfTransform.position);

                if (faceDir != Vector3.zero)
                {
                    float faceAngle = Mathf.Atan2(faceDir.y, faceDir.x) * Mathf.Rad2Deg;
                    float rotZ = faceAngle + (rotationOffset?.Value ?? 0f);

                    if (applyDirectionalRotationOffset?.Value == true)
                    {
                        rotZ += (directionalRotationOffset?.Value ?? 0f) * directionSign;
                    }

                    float rotLerp = rotationLerpSpeed?.Value ?? 0f;
                    Quaternion targetRot = Quaternion.Euler(0f, 0f, rotZ);
                    _selfTransform.rotation = (rotLerp > 0f)
                        ? Quaternion.Lerp(_selfTransform.rotation, targetRot, dt * rotLerp)
                        : targetRot;
                }
            }
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
