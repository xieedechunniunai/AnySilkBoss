using System.Collections;
using UnityEngine;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// DamageHero 事件管理器 - 负责从 Song Knight CrossSlash 预制体获取 DamageHero 组件
    /// 并提供给其他组件使用
    /// </summary>
    internal class DamageHeroEventManager : MonoBehaviour
    {
        #region Fields
        private AssetManager? _assetManager;
        private bool _initialized = false;

        // 从 Song Knight CrossSlash 预制体的 Damager1 子物体获取的 DamageHero 组件
        private DamageHero? _damageHero;
        public DamageHero? DamageHero => _damageHero;
        #endregion

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        #region Initialization
        /// <summary>
        /// 初始化 DamageHero 事件管理器
        /// </summary>
        private IEnumerator Initialize()
        {
            if (_initialized)
            {
                Log.Info("DamageHeroEventManager already initialized.");
                yield break;
            }

            Log.Info("Starting DamageHeroEventManager initialization...");

            // 获取同一 GameObject 上的 AssetManager 组件
            _assetManager = GetComponent<AssetManager>();
            if (_assetManager == null)
            {
                Log.Error("无法找到 AssetManager 组件");
                yield break;
            }
            // 从 AssetManager 获取预制体并提取 DamageHero 组件
            yield return ExtractDamageHeroFromPrefab();

            _initialized = true;
            Log.Info("DamageHeroEventManager initialization completed.");
        }

        /// <summary>
        /// 从 Song Knight CrossSlash 预制体提取 DamageHero 组件
        /// </summary>
        private IEnumerator ExtractDamageHeroFromPrefab()
        {
            var AbyssBulletPrefab = _assetManager?.Get<GameObject>("Abyss Bullet");
            if (AbyssBulletPrefab == null)
            {
                Log.Error("无法获取 'Abyss Bullet' 预制体");
                yield break;
            }

            _damageHero = AbyssBulletPrefab.GetComponent<DamageHero>();
            if (_damageHero == null)
            {
                Log.Error("'Abyss Bullet' 预制体上未找到 DamageHero 组件");
                yield break;
            }

            if (_damageHero.OnDamagedHero == null)
            {
                Log.Warn("DamageHero.OnDamagedHero 为 null，初始化为空事件");
                _damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
            }

            int listenerCount = _damageHero.OnDamagedHero.GetPersistentEventCount();
            Log.Info($"成功获取 DamageHero 组件，OnDamagedHero 监听器数量: {listenerCount}");
            Log.Info("=== DamageHero 组件提取完成 ===");
        }

        /// <summary>
        /// 递归查找子物体
        /// </summary>
        private Transform? FindChildRecursive(Transform parent, string childName)
        {
            Transform? child = parent.Find(childName);
            if (child != null)
                return child;

            foreach (Transform t in parent)
            {
                child = FindChildRecursive(t, childName);
                if (child != null)
                    return child;
            }

            return null;
        }

        /// <summary>
        /// 检查 DamageHero 组件是否已设置
        /// </summary>
        public bool HasDamageHero()
        {
            return _damageHero != null;
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public bool IsInitialized()
        {
            return _initialized;
        }
        #endregion
    }
}

