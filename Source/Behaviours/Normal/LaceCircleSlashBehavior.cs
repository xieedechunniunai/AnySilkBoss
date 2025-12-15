using UnityEngine;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    /// <summary>
    /// LaceCircleSlash 行为组件
    /// 负责在 LateUpdate 中强制覆盖动画组件控制的大小，并实现定时自动禁用
    /// </summary>
    internal class LaceCircleSlashBehavior : MonoBehaviour
    {
        #region 配置参数
        [Header("缩放配置")]
        [Tooltip("缩放倍数（相对于原始大小的倍数）")]
        public float scaleMultiplier = 2f;

        [Header("定时禁用配置")]
        [Tooltip("激活后自动禁用的时间（秒）")]
        public float deactivateTime = 2f;
        #endregion

        #region 私有字段
        private Vector3 _originalScale;      // 原始大小
        private Vector3 _targetScale;        // 目标大小
        private float _timer;                // 定时器
        private bool _initialized = false;   // 是否已初始化
        #endregion

        #region Unity 生命周期
        private void Awake()
        {
            // 记录原始大小（只在 Awake 时记录一次）
            if (!_initialized)
            {
                _originalScale = transform.localScale;
                _targetScale = _originalScale * scaleMultiplier;
                _initialized = true;
                Log.Info($"LaceCircleSlashBehavior 初始化 - 原始大小: {_originalScale}, 目标大小: {_targetScale}");
            }
        }

        private void OnEnable()
        {
            // 每次启用时重置定时器
            _timer = deactivateTime;

            // 确保目标大小已计算（防止 Awake 未执行的情况）
            if (!_initialized)
            {
                _originalScale = transform.localScale;
                _targetScale = _originalScale * scaleMultiplier;
                _initialized = true;
            }
        }

        private void Update()
        {
            // 定时器倒计时
            if (_timer > 0f)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    // 时间到，禁用物体
                    gameObject.SetActive(false);
                }
            }
        }

        private void LateUpdate()
        {
            // 在动画组件执行后强制覆盖大小
            // LateUpdate 在所有 Update 和动画更新之后执行
            transform.localScale *= scaleMultiplier;
        }
        #endregion

        #region 公开方法
        /// <summary>
        /// 设置缩放倍数（会重新计算目标大小）
        /// </summary>
        /// <param name="multiplier">缩放倍数</param>
        public void SetScaleMultiplier(float multiplier)
        {
            scaleMultiplier = multiplier;
            _targetScale = _originalScale * scaleMultiplier;
            Log.Info($"LaceCircleSlashBehavior 更新缩放倍数: {multiplier}, 新目标大小: {_targetScale}");
        }

        /// <summary>
        /// 设置禁用时间
        /// </summary>
        /// <param name="time">禁用时间（秒）</param>
        public void SetDeactivateTime(float time)
        {
            deactivateTime = time;
            _timer = time;
        }

        /// <summary>
        /// 重置定时器
        /// </summary>
        public void ResetTimer()
        {
            _timer = deactivateTime;
        }

        /// <summary>
        /// 立即禁用
        /// </summary>
        public void DeactivateNow()
        {
            _timer = 0f;
            gameObject.SetActive(false);
        }
        #endregion
    }
}

