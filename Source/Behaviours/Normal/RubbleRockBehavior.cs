using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    /// <summary>
    /// 挂载到 Rubble Field 生成的石头上，修改其 Control FSM 中 Drop 状态的 AccelerateToY 的 targetSpeed
    /// </summary>
    internal class RubbleRockBehavior : MonoBehaviour
    {
        /// <summary>
        /// 缓存的自定义 Sprite（第一次创建后缓存，后续石头直接读取）
        /// </summary>
        private static Sprite? _cachedSprite = null;

        /// <summary>
        /// AccelerateToY 的目标速度
        /// </summary>
        public float TargetSpeed = -20f;

        private void OnEnable()
        {
            ModifyDropState();
            ReplaceBoulderSprite();
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

        private void ReplaceBoulderSprite()
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
            var sprite = sprites.Find("Sprite");
            if (sprite == null)
            {
                Log.Warn("无法找到 Sprite");
                return;
            }
            var spriteRenderer = sprite.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                Log.Warn("无法找到 SpriteRenderer 组件");
                return;
            }

            // 如果缓存已存在，直接使用
            if (_cachedSprite != null)
            {
                spriteRenderer.sprite = _cachedSprite;
                return;
            }

            // 第一次：从原 Sprite 复制属性创建新 Sprite 并缓存
            var originalSprite = spriteRenderer.sprite;
            if (originalSprite == null)
            {
                Log.Warn("原始 Sprite 为 null");
                return;
            }

            // 用新 texture 创建 Sprite，复制原 Sprite 的 textureRect/pivot/pixelsPerUnit/border/extrude
            // textureRect 是原 Sprite 在 Atlas 中的区域，需要保持一致
            var textureRect = originalSprite.textureRect;
            _cachedSprite = Sprite.Create(
                Plugin.BoulderTexture,
                textureRect,
                new Vector2(originalSprite.pivot.x / originalSprite.rect.width, originalSprite.pivot.y / originalSprite.rect.height),
                originalSprite.pixelsPerUnit,
                originalSprite.extrude,
                SpriteMeshType.Tight,
                originalSprite.border
            );

            spriteRenderer.sprite = _cachedSprite;
            Log.Info("Boulder Sprite 创建并缓存完成");
        }
    }
}
