using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnySilkBoss.Source.Tools;
using GlobalEnums;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 领域结界行为组件（简化版）
    /// 使用单层结构：一个跟随 Boss 的圆形遮罩 Sprite
    /// - 圆内透明（安全区）
    /// - 圆外黑色（危险区）
    /// - 边缘羽化渐变
    /// </summary>
    internal class DomainBehavior : MonoBehaviour
    {
        #region Fields
        
        private Camera? _mainCamera;
        private Transform? _bossTransform;
        private Transform? _heroTransform;
        
        // 领域参数
        private float _currentRadius = 12f;
        private float _targetRadius = 12f;
        private bool _isActive = false;
        private bool _isShrinking = false;
        
        // 伤害检测
        private float _damageInterval = 1f;
        private float _damageTimer = 0f;
        private const int DAMAGE_AMOUNT = 2;
        
        // 配置参数
        private const float SHRINK_DURATION = 0.5f;
        private const float FEATHER_FRACTION = 0.15f; // 边缘羽化比例（半径的 15%）
        private const int SPRITE_RESOLUTION = 1024;   // 贴图分辨率
        
        // 单层遮罩对象
        private GameObject? _domainMaskObject;
        private SpriteRenderer? _domainMaskRenderer;
        private Sprite? _domainMaskSprite;
        private float _currentAlpha = 0f;
        
        // 缩圈插值
        private float _shrinkStartRadius = 12f;
        private float _shrinkElapsed = 0f;
        private float _shrinkDuration = SHRINK_DURATION;
        
        // 是否初始化成功
        private bool _initialized = false;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // 监听场景加载事件，用于场景切换后重新初始化
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // 延迟初始化，等待场景加载完成
            StartCoroutine(DelayedInitialize());
        }
        
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        
        /// <summary>
        /// 场景加载后检查并重置/重新初始化
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 只在 BOSS 场景时处理（玩家重生回 BOSS 场景）
            if (scene.name == "Cradle_03")
            {
                Log.Info($"[DomainBehavior] 检测到进入 BOSS 场景: {scene.name}，执行重置");
                
                // 检查关键对象是否被销毁
                if (_domainMaskObject == null || _domainMaskRenderer == null)
                {
                    Log.Info($"[DomainBehavior] 场景切换后检测到对象丢失，重新初始化");
                    _initialized = false;
                    StartCoroutine(DelayedInitialize());
                }
                else
                {
                    // 对象存在，只需重置状态
                    ResetDomain();
                }
            }
        }
        
        private IEnumerator DelayedInitialize()
        {
            // 等待一帧确保场景加载完成
            yield return null;
            
            Initialize();
        }
        
        private void Initialize()
        {
            // 获取主相机
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                _mainCamera = FindFirstObjectByType<Camera>();
            }
            
            // 创建单层领域遮罩
            CreateDomainMask();
            
            _initialized = true;
            Log.Info("[DomainBehavior] 初始化完成（单层结构）");
        }
        
        /// <summary>
        /// 创建领域遮罩对象（单层：圆内透明，圆外黑色，边缘羽化）
        /// </summary>
        private void CreateDomainMask()
        {
            // 清理旧对象（如果存在）
            if (_domainMaskObject != null)
            {
                Destroy(_domainMaskObject);
            }
            
            // 创建遮罩对象
            _domainMaskObject = new GameObject("Domain Mask");
            _domainMaskObject.transform.SetParent(transform);
            
            // 添加 SpriteRenderer
            _domainMaskRenderer = _domainMaskObject.AddComponent<SpriteRenderer>();
            
            // 创建遮罩贴图（圆内透明，圆外黑色，边缘羽化）
            _domainMaskSprite = CreateDomainMaskSprite(SPRITE_RESOLUTION, FEATHER_FRACTION);
            _domainMaskRenderer.sprite = _domainMaskSprite;
            
            // 设置渲染层级（确保在最上层）
            _domainMaskRenderer.sortingLayerName = "Over";
            _domainMaskRenderer.sortingOrder = 1000;
            
            // 初始透明
            _domainMaskRenderer.color = new Color(1f, 1f, 1f, 0f);
            
            // 初始隐藏
            _domainMaskObject.SetActive(false);
            
            Log.Info("[DomainBehavior] 领域遮罩对象已创建");
        }

        /// <summary>
        /// 创建领域遮罩贴图
        /// - 圆内：完全透明（alpha = 0）
        /// - 圆外：黑色不透明（alpha = 1）
        /// - 边缘：smoothstep 羽化渐变
        /// 
        /// 设计：圆只占贴图的 1/10（半径 = 贴图宽度的 1/10）
        /// 这样外面有足够的黑色区域覆盖屏幕
        /// 当缩圈到最小（如 8 单位）时，黑色区域仍有 8 * 4 = 32 单位宽度
        /// </summary>
        private Sprite CreateDomainMaskSprite(int resolution, float featherFraction)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            var pixels = new Color[resolution * resolution];
            
            float center = resolution / 2f;
            // 圆的半径 = 贴图宽度的 1/10，这样外面有大量黑色区域
            // Sprite 总宽度 = 10 * 圆直径，黑色区域从圆边缘延伸到 Sprite 边缘 = 4 * 圆半径
            float radius = resolution / 10f;
            float feather = Mathf.Max(4f, radius * Mathf.Clamp01(featherFraction));
            
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha;
                    
                    if (dist <= radius - feather)
                    {
                        // 圆内：完全透明
                        alpha = 0f;
                    }
                    else if (dist >= radius)
                    {
                        // 圆外：完全不透明（黑色）
                        alpha = 1f;
                    }
                    else
                    {
                        // 边缘：smoothstep 羽化渐变
                        float t = (dist - (radius - feather)) / feather; // 0..1
                        // smoothstep: 3t² - 2t³
                        alpha = t * t * (3f - 2f * t);
                    }
                    
                    // 黑色 + alpha
                    pixels[y * resolution + x] = new Color(0f, 0f, 0f, alpha);
                }
            }
            
            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply();
            
            // pixelsPerUnit 设置：
            // - 贴图宽度 = resolution 像素
            // - 圆的半径 = resolution / 10 像素
            // - 我们希望：当 scale = 1 时，安全区半径 = 1 世界单位
            // - 所以 pixelsPerUnit = 圆的半径（像素）/ 1（世界单位）= resolution / 10
            return Sprite.Create(
                texture, 
                new Rect(0, 0, resolution, resolution), 
                new Vector2(0.5f, 0.5f), 
                resolution / 10f
            );
        }
        
        private void Update()
        {
            if (!_isActive || !_initialized) return;
            
            // 更新遮罩位置（跟随 Boss）
            UpdateMaskPosition();
            
            // 处理缩圈动画
            if (_isShrinking)
            {
                UpdateShrinkAnimation();
            }
            
            // 检测玩家是否在危险区
            CheckPlayerDamage();
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// 激活领域结界
        /// </summary>
        /// <param name="bossPos">Boss位置</param>
        /// <param name="initialRadius">初始安全半径（世界单位）</param>
        public void ActivateDomain(Vector3 bossPos, float initialRadius)
        {
            // 检查并重新初始化（场景切换后可能丢失）
            if (!_initialized || _domainMaskObject == null || _domainMaskRenderer == null)
            {
                Log.Warn("[DomainBehavior] 检测到未初始化或对象丢失，重新初始化");
                Initialize();
            }
            
            if (_domainMaskRenderer == null)
            {
                Log.Error("[DomainBehavior] 领域遮罩组件未正确初始化，无法激活");
                return;
            }
            
            _currentRadius = initialRadius;
            _targetRadius = initialRadius;
            _isActive = true;
            _isShrinking = false;
            _damageTimer = 0f;
            _currentAlpha = 0f;
            
            // 获取 Boss Transform
            var bossObj = GameObject.Find("Silk Boss");
            if (bossObj != null)
            {
                _bossTransform = bossObj.transform;
            }
            else
            {
                Log.Warn("[DomainBehavior] 未找到 Silk Boss，使用传入的位置");
            }
            
            // 获取 Hero Transform
            var heroController = FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                _heroTransform = heroController.transform;
            }
            
            // 激活遮罩对象
            if (_domainMaskObject != null)
            {
                _domainMaskObject.SetActive(true);
            }
            
            // 设置初始位置和大小
            UpdateMaskPosition();
            UpdateMaskSize();
            
            // 初始透明
            if (_domainMaskRenderer != null)
            {
                _domainMaskRenderer.color = new Color(1f, 1f, 1f, 0f);
            }
            
            // 淡入效果
            StartCoroutine(FadeInDomain());
            
            Log.Info($"[DomainBehavior] 领域结界已激活，初始安全半径: {initialRadius}");
        }
        
        /// <summary>
        /// 缩小安全区域
        /// </summary>
        /// <param name="newRadius">新的安全半径（世界单位）</param>
        /// <param name="duration">缩小动画持续时间</param>
        public void ShrinkDomain(float newRadius, float duration = SHRINK_DURATION)
        {
            if (!_isActive) return;
            
            _targetRadius = newRadius;
            _shrinkStartRadius = _currentRadius;
            _shrinkElapsed = 0f;
            _shrinkDuration = Mathf.Max(0.01f, duration);
            _isShrinking = true;
            
            Log.Info($"[DomainBehavior] 领域结界开始缩小: {_currentRadius} -> {_targetRadius}");
        }
        
        /// <summary>
        /// 停用领域结界
        /// </summary>
        public void DeactivateDomain()
        {
            if (!_isActive) return;
            
            _isActive = false;
            _isShrinking = false;
            
            // 淡出效果
            StartCoroutine(FadeOutDomain());
            
            Log.Info("[DomainBehavior] 领域结界已停用");
        }
        
        /// <summary>
        /// 重置领域结界状态（场景重生时调用，不销毁对象）
        /// </summary>
        public void ResetDomain()
        {
            // 停止所有协程（淡入淡出等）
            StopAllCoroutines();
            
            // 重置领域参数
            _currentRadius = 12f;
            _targetRadius = 12f;
            _isActive = false;
            _isShrinking = false;
            
            // 重置伤害检测
            _damageTimer = 0f;
            
            // 重置缩圈插值
            _shrinkStartRadius = 12f;
            _shrinkElapsed = 0f;
            _shrinkDuration = SHRINK_DURATION;
            
            // 重置透明度
            _currentAlpha = 0f;
            
            // 清空引用（下次激活时重新获取）
            _bossTransform = null;
            _heroTransform = null;
            
            // 隐藏遮罩对象并重置透明度
            if (_domainMaskObject != null)
            {
                _domainMaskObject.SetActive(false);
            }
            if (_domainMaskRenderer != null)
            {
                _domainMaskRenderer.color = new Color(1f, 1f, 1f, 0f);
            }
            
            Log.Info("[DomainBehavior] 领域结界已重置");
        }
        
        #endregion

        #region Private Methods
        
        /// <summary>
        /// 更新遮罩位置（跟随 Boss）
        /// </summary>
        private void UpdateMaskPosition()
        {
            if (_domainMaskObject == null || _bossTransform == null) return;
            
            // 直接跟随 Boss 位置
            _domainMaskObject.transform.position = _bossTransform.position;
        }
        
        /// <summary>
        /// 更新遮罩大小
        /// </summary>
        private void UpdateMaskSize()
        {
            if (_domainMaskObject == null) return;
            
            // 贴图设计：
            // - 分辨率 512x512
            // - 圆的半径 = 512 / 10 = 51.2 像素
            // - pixelsPerUnit = 51.2
            // - 所以当 scale = 1 时，安全区半径 = 1 世界单位
            // - Sprite 的世界尺寸 = 512 / 51.2 = 10 单位（直径）
            // - 黑色区域从半径 1 延伸到 Sprite 边缘（半径 5）
            // 
            // 缩放逻辑：
            // - 当 scale = _currentRadius 时，安全区半径 = _currentRadius 世界单位
            // - 黑色区域从 _currentRadius 延伸到 _currentRadius * 5
            // 
            // 例如：_currentRadius = 8 时（最小缩圈）
            // - 安全区半径 = 8 单位
            // - Sprite 边缘 = 40 单位（从中心算起）
            // - 黑色区域宽度 = 32 单位，足够覆盖整个屏幕
            
            float scale = _currentRadius;
            _domainMaskObject.transform.localScale = new Vector3(scale, scale, 1f);
        }
        
        /// <summary>
        /// 更新缩圈动画
        /// </summary>
        private void UpdateShrinkAnimation()
        {
            _shrinkElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_shrinkElapsed / Mathf.Max(0.01f, _shrinkDuration));
            _currentRadius = Mathf.Lerp(_shrinkStartRadius, _targetRadius, t);
            
            // 更新遮罩大小
            UpdateMaskSize();
            
            if (t >= 1f || Mathf.Approximately(_currentRadius, _targetRadius))
            {
                _currentRadius = _targetRadius;
                _isShrinking = false;
                UpdateMaskSize();
            }
        }
        
        /// <summary>
        /// 检测玩家是否在危险区并造成伤害
        /// </summary>
        private void CheckPlayerDamage()
        {
            if (_bossTransform == null || _heroTransform == null) return;
            
            float distToBoss = Vector2.Distance(
                new Vector2(_heroTransform.position.x, _heroTransform.position.y),
                new Vector2(_bossTransform.position.x, _bossTransform.position.y)
            );
            
            if (distToBoss > _currentRadius)
            {
                // 玩家在危险区
                _damageTimer += Time.deltaTime;
                if (_damageTimer >= _damageInterval)
                {
                    // 造成伤害
                    var heroController = _heroTransform.GetComponent<HeroController>();
                    if (heroController != null)
                    {
                        heroController.TakeDamage(null, CollisionSide.other, DAMAGE_AMOUNT, HazardType.NON_HAZARD);
                        Log.Debug($"[DomainBehavior] 玩家在危险区，造成 {DAMAGE_AMOUNT} 点伤害");
                    }
                    _damageTimer = 0f;
                }
            }
            else
            {
                // 回到安全区，重置计时器
                _damageTimer = 0f;
            }
        }
        
        /// <summary>
        /// 淡入领域结界
        /// </summary>
        private IEnumerator FadeInDomain(float duration = 0.5f)
        {
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                _currentAlpha = t;
                if (_domainMaskRenderer != null)
                {
                    _domainMaskRenderer.color = new Color(1f, 1f, 1f, t);
                }
                
                yield return null;
            }
            
            _currentAlpha = 1f;
            if (_domainMaskRenderer != null)
            {
                _domainMaskRenderer.color = new Color(1f, 1f, 1f, 1f);
            }
        }
        
        /// <summary>
        /// 淡出领域结界
        /// </summary>
        private IEnumerator FadeOutDomain(float duration = 0.5f)
        {
            float elapsed = 0f;
            float startAlpha = _currentAlpha;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                _currentAlpha = Mathf.Lerp(startAlpha, 0f, t);
                if (_domainMaskRenderer != null)
                {
                    _domainMaskRenderer.color = new Color(1f, 1f, 1f, _currentAlpha);
                }
                
                yield return null;
            }
            
            _currentAlpha = 0f;
            
            // 隐藏遮罩对象
            if (_domainMaskObject != null)
            {
                _domainMaskObject.SetActive(false);
            }
            if (_domainMaskRenderer != null)
            {
                _domainMaskRenderer.color = new Color(1f, 1f, 1f, 0f);
            }
        }
        
        #endregion
    }
}
