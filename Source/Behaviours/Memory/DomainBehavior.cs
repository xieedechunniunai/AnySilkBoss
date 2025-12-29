using System.Collections;
using UnityEngine;
using AnySilkBoss.Source.Tools;
using GlobalEnums;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 领域结界行为组件
    /// 管理圆形安全区域的视觉效果和伤害检测
    /// </summary>
    internal class DomainBehavior : MonoBehaviour
    {
        #region Fields
        
        private GameObject? _faderObject;
        private SpriteRenderer? _faderRenderer;
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
        
        // 领域黑暗覆盖（使用 SpriteMask 挖洞，而不是“黑底+透明洞”）
        private GameObject? _domainDarkOverlayObject;
        private SpriteRenderer? _domainDarkOverlayRenderer;
        private GameObject? _domainCircleMaskObject;
        private SpriteMask? _domainCircleMask;
        private Sprite? _domainCircleMaskSprite;
        private float _domainOverlayAlpha = 0f;

        // 洞边缘羽化（SpriteMask 依赖贴图 alpha 做软边，比例越大过渡越明显）
        private const float HOLE_FEATHER_FRACTION = 0.12f; // 半径的 12% 用于渐变
        private const int HOLE_SPRITE_RESOLUTION = 256;

        // 渐变环：不依赖 SpriteMask 的软边（SpriteMask 通常是阈值裁切），用一张带 alpha 渐变的“黑色环”画在洞内边缘实现过渡
        private GameObject? _domainFeatherRingObject;
        private SpriteRenderer? _domainFeatherRingRenderer;
        private Sprite? _domainFeatherRingSprite;

        // 缩圈插值（按 duration 精确完成，避免当前“越接近越慢”的假线性）
        private float _shrinkStartRadius = 12f;
        private float _shrinkElapsed = 0f;
        private float _shrinkDuration = SHRINK_DURATION;
        
        // 是否初始化成功
        private bool _initialized = false;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // 延迟初始化，等待场景加载完成
            StartCoroutine(DelayedInitialize());
        }
        
        private IEnumerator DelayedInitialize()
        {
            // 等待一帧确保场景加载完成
            yield return null;
            
            Initialize();
        }
        
        private void Initialize()
        {
            // 获取主相机（优先 Camera.main）
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                _mainCamera = FindFirstObjectByType<Camera>();
            }
          
            // 创建领域遮罩（黑暗覆盖 + 圆形洞）
            CreateCircleMaskObject();
            
            _initialized = true;
            Log.Info("DomainBehavior 初始化完成");
        }
        
        /// <summary>
        /// 创建黑屏遮罩对象（如果原版不存在）
        /// </summary>
        private void CreateFaderObject()
        {
            _faderObject = new GameObject("Domain Fader");
            _faderObject.transform.SetParent(transform);
            
            _faderRenderer = _faderObject.AddComponent<SpriteRenderer>();
            _faderRenderer.sprite = CreateSquareSprite(Color.black);
            _faderRenderer.sortingLayerName = "Over";
            _faderRenderer.sortingOrder = 100;
            _faderRenderer.color = new Color(0, 0, 0, 0);
            
            // 设置足够大的尺寸覆盖整个屏幕（根据相机视野计算）
            // 使用更大的尺寸确保完全覆盖，即使相机移动也能覆盖
            float screenSize = 200f; // 大幅增加尺寸，确保覆盖整个屏幕
            _faderObject.transform.localScale = new Vector3(screenSize, screenSize, 1f);
            
            // 确保位置在相机中心（如果相机存在）
            if (_mainCamera != null)
            {
                _faderObject.transform.position = _mainCamera.transform.position;
                _faderObject.transform.position = new Vector3(
                    _faderObject.transform.position.x,
                    _faderObject.transform.position.y,
                    _faderObject.transform.position.z + 0.1f // 确保在相机前方
                );
            }
        }
        
        /// <summary>
        /// 创建圆形遮罩对象（显示安全区域）
        /// </summary>
        private void CreateCircleMaskObject()
        {
            // 1) 黑暗覆盖（全屏黑），由 SpriteMask 挖出“Boss周围透明圆洞”
            _domainDarkOverlayObject = new GameObject("Domain Dark Overlay");
            _domainDarkOverlayObject.transform.SetParent(transform);
            _domainDarkOverlayRenderer = _domainDarkOverlayObject.AddComponent<SpriteRenderer>();
            _domainDarkOverlayRenderer.sprite = CreateSquareSprite(Color.white);
            _domainDarkOverlayRenderer.sortingLayerName = "Over";
            _domainDarkOverlayRenderer.sortingOrder = 1000;
            _domainDarkOverlayRenderer.color = new Color(0, 0, 0, 0);
            _domainDarkOverlayRenderer.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            _domainDarkOverlayObject.SetActive(false);

            // 2) 圆形 SpriteMask（只负责“洞”的形状与软边）
            _domainCircleMaskObject = new GameObject("Domain Circle Hole Mask");
            _domainCircleMaskObject.transform.SetParent(transform);
            _domainCircleMask = _domainCircleMaskObject.AddComponent<SpriteMask>();
            _domainCircleMaskSprite = CreateFeatheredCircleMaskSprite(HOLE_SPRITE_RESOLUTION, HOLE_FEATHER_FRACTION);
            _domainCircleMask.sprite = _domainCircleMaskSprite;
            _domainCircleMask.isCustomRangeActive = true;
            int overLayerId = SortingLayer.NameToID("Over");
            _domainCircleMask.backSortingLayerID = overLayerId;
            _domainCircleMask.frontSortingLayerID = overLayerId;
            // 影响 overlay(1000) 与“洞内渐变环”(1002)
            _domainCircleMask.backSortingOrder = 998;
            _domainCircleMask.frontSortingOrder = 1004;
            _domainCircleMaskObject.SetActive(false);

            // 3) 洞内渐变环（只在洞内显示）：用于制造“透明→黑”的过渡
            _domainFeatherRingObject = new GameObject("Domain Feather Ring");
            _domainFeatherRingObject.transform.SetParent(transform);
            _domainFeatherRingRenderer = _domainFeatherRingObject.AddComponent<SpriteRenderer>();
            _domainFeatherRingSprite = CreateFeatherRingSprite(HOLE_SPRITE_RESOLUTION, HOLE_FEATHER_FRACTION);
            _domainFeatherRingRenderer.sprite = _domainFeatherRingSprite;
            _domainFeatherRingRenderer.sortingLayerName = "Over";
            _domainFeatherRingRenderer.sortingOrder = 1002;
            _domainFeatherRingRenderer.color = new Color(0, 0, 0, 0);
            _domainFeatherRingRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            _domainFeatherRingObject.SetActive(false);

        }

        /// <summary>
        /// 创建用于 SpriteMask 的“羽化圆洞”贴图：
        /// - 圆内 alpha=1
        /// - 圆外 alpha=0
        /// - 边缘按 featherFraction 做平滑渐变（过渡更明显）
        /// </summary>
        private Sprite CreateFeatheredCircleMaskSprite(int resolution, float featherFraction)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            var pixels = new Color[resolution * resolution];

            float center = resolution / 2f;
            float radius = resolution / 2f - 2f;
            float feather = Mathf.Max(2f, radius * Mathf.Clamp01(featherFraction));

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float a;
                    if (dist <= radius - feather)
                    {
                        a = 1f;
                    }
                    else if (dist >= radius)
                    {
                        a = 0f;
                    }
                    else
                    {
                        float t = (dist - (radius - feather)) / feather; // 0..1
                        // 更柔和的过渡（比线性更像 shader 的 smoothstep）
                        float s = t * t * (3f - 2f * t);
                        a = 1f - s;
                    }

                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, a);
                }
            }

            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution / 2f);
        }
        
        /// <summary>
        /// 创建“洞内渐变环”贴图（黑色+alpha 渐变）：
        /// - 内圈 alpha=0（保持透明）
        /// - 由内向外在 feather 区间内 alpha 逐渐变为 1（靠近洞边越黑）
        /// - 外圈 alpha=0（洞外不额外叠加，避免影响外侧纯黑）
        /// 该环会被 SpriteMask 限制为“只在洞内可见”，从而实现透明到黑的过渡。
        /// </summary>
        private Sprite CreateFeatherRingSprite(int resolution, float featherFraction)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            var pixels = new Color[resolution * resolution];

            float center = resolution / 2f;
            float radius = resolution / 2f - 2f; // 外半径
            float feather = Mathf.Max(2f, radius * Mathf.Clamp01(featherFraction));
            float inner = Mathf.Max(0f, radius - feather);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float a;
                    if (dist < inner)
                    {
                        a = 0f;
                    }
                    else if (dist > radius)
                    {
                        a = 0f;
                    }
                    else
                    {
                        float t = (dist - inner) / Mathf.Max(0.0001f, (radius - inner)); // 0..1
                        // smoothstep
                        float s = t * t * (3f - 2f * t);
                        a = s;
                    }

                    pixels[y * resolution + x] = new Color(0f, 0f, 0f, a);
                }
            }

            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution / 2f);
        }

        /// <summary>
        /// 创建正方形 Sprite
        /// </summary>
        private Sprite CreateSquareSprite(Color color)
        {
            var texture = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            
            return Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f);
        }
        
        /// <summary>
        /// 创建圆形遮罩 Sprite（圆内透明，圆外黑色）
        /// </summary>
        private Sprite CreateCircleSprite(int resolution, Color innerColor, Color outerColor)
        {
            var texture = new Texture2D(resolution, resolution);
            var pixels = new Color[resolution * resolution];
            
            float center = resolution / 2f;
            float radius = resolution / 2f - 2f; // 留一点边缘
            
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    
                    if (dist < radius - 1f)
                    {
                        // 圆内：透明
                        pixels[y * resolution + x] = innerColor;
                    }
                    else if (dist < radius + 1f)
                    {
                        // 边缘：渐变
                        float t = (dist - (radius - 1f)) / 2f;
                        pixels[y * resolution + x] = Color.Lerp(innerColor, outerColor, t);
                    }
                    else
                    {
                        // 圆外：黑色
                        pixels[y * resolution + x] = outerColor;
                    }
                }
            }
            
            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply();
            
            return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution / 2f);
        }
        
        private void Update()
        {
            if (!_isActive || !_initialized) return;


            
            // 更新黑暗覆盖（跟随相机，确保总是覆盖屏幕；避免 nearClip 裁剪）
            UpdateDomainOverlayTransform();
            
            // 更新圆形遮罩位置（跟随Boss）
            UpdateCircleMaskPosition();
            
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
            if (!_initialized)
            {
                Log.Warn("DomainBehavior 尚未初始化，尝试立即初始化");
                Initialize();
            }
            
            if (_domainDarkOverlayRenderer == null || _domainCircleMask == null || _domainCircleMaskSprite == null)
            {
                Log.Error("领域结界组件未正确初始化，无法激活");
                return;
            }
            
            _currentRadius = initialRadius;
            _targetRadius = initialRadius;
            _isActive = true;
            _isShrinking = false;
            _damageTimer = 0f;
            
            // 获取Boss和Hero的Transform
            var bossObj = GameObject.Find("Silk Boss");
            if (bossObj != null)
            {
                _bossTransform = bossObj.transform;
            }
            
            var heroController = FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                _heroTransform = heroController.transform;
            }
            
            // 激活黑暗覆盖与圆形洞 Mask
            if (_domainDarkOverlayObject != null) _domainDarkOverlayObject.SetActive(true);
            if (_domainCircleMaskObject != null) _domainCircleMaskObject.SetActive(true);
            if (_domainFeatherRingObject != null) _domainFeatherRingObject.SetActive(true);
            _domainOverlayAlpha = 0f;
            if (_domainDarkOverlayRenderer != null)
            {
                _domainDarkOverlayRenderer.color = new Color(0, 0, 0, 0);
            }
            if (_domainFeatherRingRenderer != null)
            {
                _domainFeatherRingRenderer.color = new Color(0, 0, 0, 0);
            }
            UpdateDomainOverlayTransform();
            
            // 设置圆形遮罩初始大小
            UpdateCircleMaskSize();
            
            // 确保圆形洞位置正确
            UpdateCircleMaskPosition();
           
            // 淡入效果
            StartCoroutine(FadeInDomain());
            
            Log.Info($"领域结界已激活，初始安全半径: {initialRadius}");
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
            
            Log.Info($"领域结界开始缩小: {_currentRadius} -> {_targetRadius}");
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
            
            Log.Info("领域结界已停用");
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// 更新圆形遮罩位置（跟随Boss）
        /// </summary>
        private void UpdateCircleMaskPosition()
        {
            if (_domainCircleMaskObject == null || _bossTransform == null) return;
            _domainCircleMaskObject.transform.position = _bossTransform.position;
            if (_domainFeatherRingObject != null)
            {
                _domainFeatherRingObject.transform.position = _bossTransform.position;
            }
        }
        
        /// <summary>
        /// 更新圆形遮罩大小
        /// </summary>
        private void UpdateCircleMaskSize()
        {
            if (_domainCircleMaskObject == null) return;
            
            // 圆形遮罩的大小计算
            // CreateCircleSprite 中 pixelsPerUnit = resolution / 2 = 64 / 2 = 32
            // 这意味着 Sprite 的 1 世界单位 = 32 像素
            // Sprite 的尺寸是 64x64 像素，所以 Sprite 的尺寸（世界单位）= 64 / 32 = 2 单位
            // Sprite 的半径（世界单位）= 1 单位
            // 要让这个半径 1 单位的圆代表 _currentRadius 世界单位，需要：
            // scale = _currentRadius / 1 = _currentRadius
            // 但由于 Sprite 的尺寸是直径，所以实际需要：
            // scale = _currentRadius * 2 / 2 = _currentRadius
            // 但考虑到 Sprite 的实际显示效果，使用：
            float scale = _currentRadius; // 直接使用半径作为缩放值
            _domainCircleMaskObject.transform.localScale = new Vector3(scale, scale, 1f);
            if (_domainFeatherRingObject != null)
            {
                _domainFeatherRingObject.transform.localScale = new Vector3(scale, scale, 1f);
            }
            
            Log.Debug($"更新圆形遮罩大小: 半径={_currentRadius}, 缩放={scale}");
        }
        
        /// <summary>
        /// 更新缩圈动画
        /// </summary>
        private void UpdateShrinkAnimation()
        {
            _shrinkElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_shrinkElapsed / Mathf.Max(0.01f, _shrinkDuration));
            _currentRadius = Mathf.Lerp(_shrinkStartRadius, _targetRadius, t);

            if (t >= 1f || Mathf.Approximately(_currentRadius, _targetRadius))
            {
                _isShrinking = false;

                return;
            }
            
            // 更新圆形遮罩大小
            UpdateCircleMaskSize();
        }

        /// <summary>
        /// 更新领域黑暗覆盖（跟随相机并覆盖屏幕；避免 nearClip 裁剪）
        /// </summary>
        private void UpdateDomainOverlayTransform()
        {
            if (_domainDarkOverlayObject == null || _domainDarkOverlayRenderer == null || _mainCamera == null) return;

            // 放在 nearClip 后一点点，确保不会被裁剪；同时始终面对相机
            float dist = Mathf.Min(_mainCamera.nearClipPlane + 0.5f, _mainCamera.farClipPlane - 0.5f);
            Vector3 camPos = _mainCamera.transform.position;
            Vector3 camFwd = _mainCamera.transform.forward;
            _domainDarkOverlayObject.transform.position = camPos + camFwd * dist;
            _domainDarkOverlayObject.transform.rotation = _mainCamera.transform.rotation;

            // 计算该距离处需要覆盖的世界尺寸
            float viewH;
            if (_mainCamera.orthographic)
            {
                viewH = _mainCamera.orthographicSize * 2f;
            }
            else
            {
                // 透视：用 FOV 计算截面高度
                viewH = 2f * dist * Mathf.Tan(_mainCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            }
            float viewW = viewH * _mainCamera.aspect;

            // 我们的方块 sprite 是 4x4 像素，ppu=1 => 世界尺寸 4
            float spriteWorldSize = 4f;
            float scaleX = (viewW / spriteWorldSize) * 1.1f; // 留 10% 余量，避免边缘漏光
            float scaleY = (viewH / spriteWorldSize) * 1.1f;
            _domainDarkOverlayObject.transform.localScale = new Vector3(scaleX, scaleY, 1f);
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
                        Log.Debug($"玩家在危险区，造成 {DAMAGE_AMOUNT} 点伤害");
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

                _domainOverlayAlpha = t;
                if (_domainDarkOverlayRenderer != null)
                {
                    _domainDarkOverlayRenderer.color = new Color(0, 0, 0, t);
                }
                if (_domainFeatherRingRenderer != null)
                {
                    _domainFeatherRingRenderer.color = new Color(0, 0, 0, t);
                }
                
                yield return null;
            }
            
            _domainOverlayAlpha = 1f;
            if (_domainDarkOverlayRenderer != null) _domainDarkOverlayRenderer.color = new Color(0, 0, 0, 1f);
            if (_domainFeatherRingRenderer != null) _domainFeatherRingRenderer.color = new Color(0, 0, 0, 1f);

        }
        
        /// <summary>
        /// 淡出领域结界
        /// </summary>
        private IEnumerator FadeOutDomain(float duration = 0.5f)
        {
            float elapsed = 0f;
            Color startOverlayColor = _domainDarkOverlayRenderer != null ? _domainDarkOverlayRenderer.color : new Color(0, 0, 0, _domainOverlayAlpha);
           
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                _domainOverlayAlpha = Mathf.Lerp(startOverlayColor.a, 0f, t);
                if (_domainDarkOverlayRenderer != null)
                {
                    _domainDarkOverlayRenderer.color = Color.Lerp(startOverlayColor, new Color(0, 0, 0, 0), t);
                }
                if (_domainFeatherRingRenderer != null)
                {
                    _domainFeatherRingRenderer.color = new Color(0, 0, 0, _domainOverlayAlpha);
                }
                
                yield return null;
            }
            
            _domainOverlayAlpha = 0f;

            // 隐藏遮罩与黑暗覆盖（不再操作 Beast Slash Fader，避免与其它招式冲突）
            if (_domainCircleMaskObject != null) _domainCircleMaskObject.SetActive(false);
            if (_domainDarkOverlayObject != null) _domainDarkOverlayObject.SetActive(false);
            if (_domainDarkOverlayRenderer != null) _domainDarkOverlayRenderer.color = new Color(0, 0, 0, 0);
            if (_domainFeatherRingObject != null) _domainFeatherRingObject.SetActive(false);
            if (_domainFeatherRingRenderer != null) _domainFeatherRingRenderer.color = new Color(0, 0, 0, 0);


        }
        
        #endregion
    }
}

