using UnityEngine;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 梦境版 Hand 控制器
    /// 在普通版基础上，使用 MemoryFingerBladeBehavior 替代普通版
    /// </summary>
    internal class MemoryHandControlBehavior : HandControlBehavior
    {
        /// <summary>
        /// 创建梦境版 FingerBlade Behavior
        /// </summary>
        protected override FingerBladeBehavior CreateFingerBladeBehavior(GameObject bladeObj)
        {
            Log.Debug($"[MemoryHand] 创建 MemoryFingerBladeBehavior: {bladeObj.name}");
            return bladeObj.AddComponent<MemoryFingerBladeBehavior>();
        }

        /// <summary>
        /// 初始化梦境版 FingerBlade
        /// </summary>
        protected override void InitializeFingerBlade(FingerBladeBehavior bladeBehavior, int bladeIndex)
        {
            // MemoryFingerBladeBehavior 使用 new 关键字隐藏了基类的 Initialize
            // 需要显式调用正确的版本
            if (bladeBehavior is MemoryFingerBladeBehavior memoryBlade)
            {
                memoryBlade.Initialize(bladeIndex, handName, this);
                Log.Debug($"[MemoryHand] 已初始化 MemoryFingerBladeBehavior {bladeIndex}");
            }
            else
            {
                base.InitializeFingerBlade(bladeBehavior, bladeIndex);
            }
        }
    }
}


