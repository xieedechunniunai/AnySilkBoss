using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using System.Linq;
namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 管理 Lace Boss 的加载、缓存和实例化
    /// 参考 SilkenSisters 的实现模式
    /// </summary>
    internal sealed class LaceBossManager : MonoBehaviour
    {

        #region Fields
        // 缓存字段（预加载的原始对象）
        public GameObject LaceBossSceneCache { get; private set; } = null;
        public GameObject LaceBoss2Cache { get; private set; } = null;

        // 实例字段（当前使用的实例）
        public GameObject LaceBossSceneInstance { get; private set; } = null;
        public GameObject LaceBoss2Instance { get; private set; } = null;

        // FSM 拥有者引用
        public FsmOwnerDefault LaceBossFsmOwner { get; private set; } = null;

        // 加载状态
        private bool _isLoaded = false;
        private bool _isLoading = false;
        #endregion

        #region Public Methods
        /// <summary>
        /// 预加载 Lace Boss 相关对象到缓存
        /// </summary>
        public async Task PreloadLaceBoss()
        {
            if (_isLoaded)
            {
                Log.Info("[LaceBossManager] Lace Boss 已经预加载，跳过");
                return;
            }

            if (_isLoading)
            {
                Log.Warn("[LaceBossManager] 正在加载中，请等待");
                return;
            }

            _isLoading = true;

            try
            {
                Log.Info("[LaceBossManager] 开始预加载 Lace Boss...");

                // 从 Song_Tower_01 场景加载 Boss Scene
                LaceBossSceneCache = await SceneObjectManager.LoadObjectFromScene(
                    "Song_Tower_01",  // 场景名称
                    "Boss Scene"      // 场景中的对象路径
                );

                if (LaceBossSceneCache == null)
                {
                    Log.Error("[LaceBossManager] 无法加载 Boss Scene");
                    _isLoading = false;
                    return;
                }

                Log.Info("[LaceBossManager] 成功加载 Boss Scene 缓存");

                // 从 Boss Scene 中找到 Lace Boss2 New
                LaceBoss2Cache = SceneObjectManager.FindChildObject(
                    LaceBossSceneCache,
                    "Lace Boss2 New"
                );

                if (LaceBoss2Cache == null)
                {
                    Log.Error("[LaceBossManager] 无法在 Boss Scene 中找到 Lace Boss2 New");
                    _isLoading = false;
                    return;
                }

                Log.Info("[LaceBossManager] 成功找到 Lace Boss2 New 缓存");

                // 禁用可能的自动激活组件
                var deactivateComponent = LaceBoss2Cache.GetComponent<DeactivateIfPlayerdataTrue>();
                if (deactivateComponent != null)
                {
                    deactivateComponent.enabled = false;
                    Log.Info("[LaceBossManager] 禁用 DeactivateIfPlayerdataTrue 组件");
                }

                _isLoaded = true;
                Log.Info("[LaceBossManager] Lace Boss 预加载完成");
            }
            catch (Exception ex)
            {
                Log.Error($"[LaceBossManager] 预加载失败: {ex}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// 实例化 Lace Boss（从缓存）
        /// </summary>
        /// <param name="position">生成位置</param>
        /// <param name="activateImmediately">是否立即激活</param>
        /// <returns>是否成功实例化</returns>
        public bool SpawnLaceBoss(Vector3 position, bool activateImmediately = false)
        {
            if (!_isLoaded)
            {
                Log.Error("[LaceBossManager] Lace Boss 尚未预加载，请先调用 PreloadLaceBoss");
                return false;
            }

            try
            {
                Log.Info("[LaceBossManager] 开始实例化 Lace Boss Scene...");

                // 实例化 Boss Scene
                LaceBossSceneInstance = Instantiate(LaceBossSceneCache);
                LaceBossSceneInstance.SetActive(true);

                Log.Info("[LaceBossManager] 成功实例化 Boss Scene");

                // 找到 Lace Boss2 实例
                LaceBoss2Instance = SceneObjectManager.FindChildObject(
                    LaceBossSceneInstance,
                    "Lace Boss2 New"
                );

                if (LaceBoss2Instance == null)
                {
                    Log.Error("[LaceBossManager] 无法在实例中找到 Lace Boss2 New");
                    return false;
                }

                // 设置位置
                LaceBoss2Instance.transform.position = position;

                // 设置 FSM 拥有者引用
                LaceBossFsmOwner = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    GameObject = new FsmGameObject { Value = LaceBoss2Instance }
                };

                Log.Info($"[LaceBossManager] Lace Boss2 实例化成功，位置: {position}");

                // 根据参数决定是否激活
                if (activateImmediately)
                {
                    ActivateLaceBoss();
                }
                else
                {
                    LaceBoss2Instance.SetActive(false);
                    Log.Info("[LaceBossManager] Lace Boss2 已设置为不激活状态");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[LaceBossManager] 实例化失败: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 激活 Lace Boss 并开始战斗
        /// </summary>
        /// <param name="startEvent">要发送的启动事件（默认为 "BATTLE START FIRST"）</param>
        public void ActivateLaceBoss(string startEvent = "BATTLE START FIRST")
        {
            if (LaceBoss2Instance == null)
            {
                Log.Error("[LaceBossManager] Lace Boss2 实例不存在");
                return;
            }

            try
            {
                LaceBoss2Instance.SetActive(true);
                Log.Info("[LaceBossManager] Lace Boss2 已激活");

                // 发送启动事件
                var fsm = LaceBoss2Instance.GetComponent<PlayMakerFSM>();
                if (fsm != null && !string.IsNullOrEmpty(startEvent))
                {
                    fsm.SendEvent(startEvent);
                    Log.Info($"[LaceBossManager] 发送事件: {startEvent}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[LaceBossManager] 激活失败: {ex}");
            }
        }

        /// <summary>
        /// 销毁当前 Lace Boss 实例
        /// </summary>
        public void DestroyLaceBossInstance()
        {
            if (LaceBossSceneInstance != null)
            {
                Destroy(LaceBossSceneInstance);
                LaceBossSceneInstance = null;
                LaceBoss2Instance = null;
                LaceBossFsmOwner = null;
                Log.Info("[LaceBossManager] Lace Boss 实例已销毁");
            }
        }

        /// <summary>
        /// 清空所有缓存
        /// </summary>
        public void ClearCache()
        {
            DestroyLaceBossInstance();

            if (LaceBossSceneCache != null)
            {
                Destroy(LaceBossSceneCache);
                LaceBossSceneCache = null;
            }

            if (LaceBoss2Cache != null)
            {
                LaceBoss2Cache = null; // 这是 LaceBossSceneCache 的子对象，会一起被销毁
            }

            _isLoaded = false;
            Log.Info("[LaceBossManager] 缓存已清空");
        }
        #endregion
    }
}
