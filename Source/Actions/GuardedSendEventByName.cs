using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;
using HutongGames.PlayMaker;
using UnityEngine;

namespace AnySilkBoss.Source.Actions
{
    public class GuardedSendEventByName : FsmStateAction
    {
        public FsmEventTarget? eventTarget;
        public FsmString? sendEvent;
        public FsmFloat? delay;
        public bool everyFrame;
        public FsmBool? suppressDuringBigSilkBall;

        private float _elapsed;

        public override void Reset()
        {
            eventTarget = null;
            sendEvent = null;
            delay = null;
            everyFrame = false;
            suppressDuringBigSilkBall = new FsmBool(true);
        }

        public override void OnEnter()
        {
            _elapsed = 0f;
            if ((delay?.Value ?? 0f) < 0.001f)
            {
                SendIfAllowed();
                if (!everyFrame)
                {
                    Finish();
                }
            }
        }

        public override void OnUpdate()
        {
            if (everyFrame)
            {
                SendIfAllowed();
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= (delay?.Value ?? 0f))
            {
                SendIfAllowed();
                Finish();
            }
        }

        private void SendIfAllowed()
        {
            string eventName = sendEvent?.Value ?? "";
            if ((suppressDuringBigSilkBall?.Value ?? true) &&
                eventName == "ATTACK" &&
                BigSilkBallPhaseGuard.IsActive)
            {
                AnySilkBoss.Source.Tools.Log.Info("[GuardedSendEventByName] 大丝球阶段抑制延迟 Web Strand ATTACK");
                return;
            }

            if (eventTarget != null && !string.IsNullOrEmpty(eventName))
            {
                Fsm.Event(eventTarget, eventName);
            }
        }
    }
}
