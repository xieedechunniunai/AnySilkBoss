using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 管理从游戏场景中动态加载对象的工具类
    /// 参考 SilkenSisters 的实现
    /// </summary>
    internal static class SceneObjectManager
    {
        /// <summary>
        /// 场景 bundle 文件夹路径
        /// </summary>
        private static readonly string SceneFolder = Path.Combine(
            Application.streamingAssetsPath,
            "aa",
            Application.platform switch
            {
                RuntimePlatform.WindowsPlayer => "StandaloneWindows64",
                RuntimePlatform.OSXPlayer => "StandaloneOSX",
                RuntimePlatform.LinuxPlayer => "StandaloneLinux64",
                _ => ""
            },
            "scenes_scenes_scenes"
        );

        /// <summary>
        /// 从指定场景中加载并提取对象
        /// </summary>
        /// <param name="sceneName">场景名称（如 "Song_Tower_01"）</param>
        /// <param name="objectPath">对象路径（如 "Boss Scene/Lace Boss2 New"）</param>
        /// <returns>提取的对象副本，设置为 DontDestroyOnLoad</returns>
        public static async Task<GameObject> LoadObjectFromScene(string sceneName, string objectPath)
        {
            GameObject objectCopy = null;

            try
            {
                Log.Info($"[SceneObjectManager] 当前场景: {SceneManager.GetActiveScene().name}");
                Log.Info($"[SceneObjectManager] 开始加载场景: {sceneName}");

                // 1. 加载场景的 AssetBundle
                string bundlePath = Path.Combine(SceneFolder, $"{sceneName}.bundle".ToLower());
                
                if (!File.Exists(bundlePath))
                {
                    Log.Error($"[SceneObjectManager] 场景 bundle 文件不存在: {bundlePath}");
                    return null;
                }

                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                
                if (bundle == null)
                {
                    Log.Error($"[SceneObjectManager] 无法加载场景 bundle: {bundlePath}");
                    return null;
                }

                // 2. 异步加载场景（作为附加场景）
                await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                Scene scene = SceneManager.GetSceneByName(sceneName);
                
                if (!scene.isLoaded)
                {
                    Log.Error($"[SceneObjectManager] 场景加载失败: {sceneName}");
                    bundle.Unload(true);
                    return null;
                }

                Log.Info($"[SceneObjectManager] 场景 {scene.name} 加载成功");

                // 3. 从场景中查找目标对象
                GameObject targetObject = FindObjectInScene(scene, objectPath);
                
                if (targetObject == null)
                {
                    Log.Error($"[SceneObjectManager] 在场景 {sceneName} 中未找到对象: {objectPath}");
                    await SceneManager.UnloadSceneAsync(scene.name);
                    bundle.Unload(true);
                    return null;
                }

                // 4. 实例化对象并设置为不销毁
                objectCopy = UnityEngine.Object.Instantiate(targetObject);
                UnityEngine.Object.DontDestroyOnLoad(objectCopy);
                
                Log.Info($"[SceneObjectManager] 成功实例化对象: {objectCopy.name}");

                // 5. 卸载临时场景和 bundle
                Log.Info($"[SceneObjectManager] 卸载场景: {scene.name}");
                await SceneManager.UnloadSceneAsync(scene.name);
                
                Log.Info($"[SceneObjectManager] 卸载 bundle: {bundle.name}");
                await bundle.UnloadAsync(false);

                // 6. 设置为不激活状态，等待后续使用
                objectCopy.SetActive(false);

                Log.Info($"[SceneObjectManager] 对象加载完成: {objectPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[SceneObjectManager] 加载对象时发生异常: {ex}");
                return null;
            }

            return objectCopy;
        }

        /// <summary>
        /// 在场景中查找对象（支持路径查找）
        /// </summary>
        /// <param name="scene">目标场景</param>
        /// <param name="objectPath">对象路径（用 / 分隔层级，如 "Parent/Child"）</param>
        /// <returns>找到的对象，未找到返回 null</returns>
        public static GameObject FindObjectInScene(Scene scene, string objectPath)
        {
            int objectIndex = 0;
            string[] hierarchy = objectPath.Split('/');

            Log.Debug($"[SceneObjectManager] 在场景 {scene.name} 中搜索对象: {objectPath}");
            Log.Debug($"[SceneObjectManager] 场景根对象数量: {scene.GetRootGameObjects().Length}");

            // 从根对象开始查找
            GameObject currentObject = scene.GetRootGameObjects()
                .FirstOrDefault(obj => obj.name == hierarchy[objectIndex]);

            if (currentObject == null)
            {
                Log.Error($"[SceneObjectManager] 未找到根对象: {hierarchy[objectIndex]}");
                return null;
            }

            objectIndex++;

            // 沿层级向下查找
            while (objectIndex < hierarchy.Length)
            {
                Log.Debug($"[SceneObjectManager] 查找子对象: {hierarchy[objectIndex]}");
                
                Transform childTransform = currentObject.transform
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(tf => tf.name == hierarchy[objectIndex]);

                if (childTransform == null)
                {
                    Log.Error($"[SceneObjectManager] 未找到子对象: {hierarchy[objectIndex]}");
                    return null;
                }

                currentObject = childTransform.gameObject;
                objectIndex++;
            }

            Log.Info($"[SceneObjectManager] 找到对象: {currentObject.name}");
            return currentObject;
        }

        /// <summary>
        /// 在当前激活场景中查找对象
        /// </summary>
        /// <param name="objectPath">对象路径</param>
        /// <returns>找到的对象</returns>
        public static GameObject FindObjectInCurrentScene(string objectPath)
        {
            return FindObjectInScene(SceneManager.GetActiveScene(), objectPath);
        }

        /// <summary>
        /// 在指定 GameObject 的子对象中查找（支持路径）
        /// </summary>
        /// <param name="parent">父对象</param>
        /// <param name="childPath">子对象路径（用 / 分隔层级）</param>
        /// <returns>找到的子对象，未找到返回 null</returns>
        public static GameObject FindChildObject(GameObject parent, string childPath)
        {
            if (parent == null)
            {
                Log.Error("[SceneObjectManager] 父对象为 null");
                return null;
            }

            int objectIndex = 0;
            string[] hierarchy = childPath.Split('/');
            GameObject currentObject = parent;

            while (objectIndex < hierarchy.Length)
            {
                Log.Debug($"[SceneObjectManager] 查找子对象: {hierarchy[objectIndex]}");
                
                Transform childTransform = currentObject.transform
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(tf => tf.name == hierarchy[objectIndex]);

                if (childTransform == null)
                {
                    Log.Error($"[SceneObjectManager] 未找到子对象: {hierarchy[objectIndex]}");
                    return null;
                }

                currentObject = childTransform.gameObject;
                objectIndex++;
            }

            Log.Debug($"[SceneObjectManager] 找到子对象: {currentObject.name}");
            return currentObject;
        }
    }
}
