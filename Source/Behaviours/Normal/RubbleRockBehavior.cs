using System;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    /// <summary>
    /// 挂载到 Rubble Field 生成的石头上，修改其 Control FSM 中 Drop 状态的 AccelerateToY 的 targetSpeed
    /// 支持 Mid 和 Large 两种石头，每种有不同的 Sprite 子物品
    /// </summary>
    internal class RubbleRockBehavior : MonoBehaviour
    {
        // （调试 instrumentation 已清理；仅保留修复逻辑）

        /// <summary>
        /// AccelerateToY 的目标速度
        /// </summary>
        public float TargetSpeed = -19f;

        /// <summary>
        /// Mid 石头的 Sprite 子物品名称列表
        /// </summary>
        private static readonly string[] MidSpriteNames = { "Sprite", "Sprite type 2", "Sprite type 3" };

        /// <summary>
        /// Large 石头的 Sprite 子物品名称列表
        /// </summary>
        private static readonly string[] LargeSpriteNames = { "Sprite", "Sprite type 2" };

        private static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        // #region material override strategy (keep original sprite, only swap atlas texture)
        private static bool TryOverrideSpriteMainTex(SpriteRenderer sr, Texture2D newAtlas, out string reason)
        {
            reason = "";
            try
            {
                if (sr == null) { reason = "sr null"; return false; }
                if (newAtlas == null) { reason = "atlas null"; return false; }
                if (sr.sharedMaterial == null) { reason = "sharedMaterial null"; return false; }

                // Sprite/Default uses _MainTex
                const string prop = "_MainTex";
                if (!sr.sharedMaterial.HasProperty(prop))
                {
                    reason = $"material has no {prop}";
                    return false;
                }

                sr.GetPropertyBlock(_mpb);
                _mpb.SetTexture(prop, newAtlas);
                sr.SetPropertyBlock(_mpb);

                // verify by reading back the block we just set
                sr.GetPropertyBlock(_mpb);
                var got = _mpb.GetTexture(prop);
                if (got != newAtlas)
                {
                    reason = "mpb set but readback mismatch";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = $"{ex.GetType().Name}:{ex.Message}";
                return false;
            }
        }
        // #endregion

        private void OnEnable()
        {
            ModifyDropState();
            ReplaceBoulderSprites();
        }

        private void ModifyDropState()
        {
            // 查找名为 "Control" 的 PlayMakerFSM
            var controlFsm = GetComponents<PlayMakerFSM>()
                .FirstOrDefault(fsm => fsm.FsmName == "Control");

            if (controlFsm == null) return;

            // 查找 "Drop" 状态
            var dropState = controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Drop");
            if (dropState == null) return;

            // 查找第一个 AccelerateToY 行为并修改 targetSpeed
            var accelerateAction = dropState.Actions.OfType<AccelerateToY>().FirstOrDefault();
            if (accelerateAction != null)
            {
                accelerateAction.targetSpeed = TargetSpeed;
            }
        }

        private void ReplaceBoulderSprites()
        {
            if (Plugin.BoulderTexture == null)
            {
                Log.Warn("Boulder 贴图未加载");
                return;
            }

            var boulder = transform.Find("boulder");
            if (boulder == null)
            {
                Log.Warn("无法找到 boulder");
                return;
            }

            var sprites = boulder.Find("Sprites");
            if (sprites == null)
            {
                Log.Warn("无法找到 Sprites");
                return;
            }

            // 判断是 Mid 还是 Large 石头
            bool isLarge = gameObject.name.Contains("Large");
            string[] spriteNames = isLarge ? LargeSpriteNames : MidSpriteNames;

            // 替换所有 Sprite 子物品
            foreach (var spriteName in spriteNames)
            {
                var spriteTransform = sprites.Find(spriteName);
                if (spriteTransform == null)
                {
                    Log.Warn($"无法找到 {spriteName}");
                    continue;
                }

                var spriteRenderer = spriteTransform.GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    Log.Warn($"无法找到 {spriteName} 的 SpriteRenderer 组件");
                    continue;
                }

                var originalSprite = spriteRenderer.sprite;
                if (originalSprite == null)
                {
                    Log.Warn($"{spriteName} 的原始 Sprite 为 null");
                    continue;
                }

                if (!TryOverrideSpriteMainTex(spriteRenderer, Plugin.BoulderTexture, out var reason))
                {
                    Log.Warn($"[{gameObject.name}] 覆盖 {spriteName} 的 _MainTex 失败：{reason}");
                    continue;
                }

                // Large 石头有 tint 的情况会把贴图乘色，导致颜色不对；这里强制还原为白色
                //（Mid 保持原行为，避免不必要的改动）
                if (isLarge)
                {
                    spriteRenderer.color = Color.white;
                }
            }
        }
    }
}
