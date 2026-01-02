using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    internal partial class AttackControlBehavior
    {
        #region 爬升阶段攻击系统
        private void CreateClimbPhaseAttackStates()
        {
            if (_attackControlFsm == null) return;

            Log.Info("=== 开始创建爬升阶段攻击状态链 ===");

            var climbStates = CreateStates(_attackControlFsm.Fsm,
                ("Climb Attack Choice", "爬升阶段攻击选择"),
                ("Climb Needle Attack", "爬升阶段针攻击"),
                ("Climb Web Attack", "爬升阶段网攻击"),
                ("Climb Silk Ball Attack", "爬升阶段丝球攻击"),
                ("Climb Attack Cooldown", "爬升阶段攻击冷却")
            );
            AddStatesToFsm(_attackControlFsm, climbStates);

            var climbAttackChoice = climbStates[0];
            var climbNeedleAttack = climbStates[1];
            var climbWebAttack = climbStates[2];
            var climbSilkBallAttack = climbStates[3];
            var climbAttackCooldown = climbStates[4];

            var idleState = FindState(_attackControlFsm, "Idle");

            AddClimbAttackChoiceActions(climbAttackChoice);
            AddClimbNeedleAttackActions(climbNeedleAttack);
            AddClimbWebAttackActions(climbWebAttack);
            AddClimbSilkBallAttackActions(climbSilkBallAttack);
            AddClimbAttackCooldownActions(climbAttackCooldown);

            AddClimbAttackTransitions(climbAttackChoice, climbNeedleAttack,
                climbWebAttack, climbSilkBallAttack, climbAttackCooldown);

            AddClimbPhaseAttackGlobalTransitions(climbAttackChoice, idleState);

            Log.Info("=== 爬升阶段攻击状态链创建完成 ===");
        }

        private void AddClimbAttackChoiceActions(FsmState choiceState)
        {
            var actions = new List<FsmStateAction>();

            var climbNeedleEvent = FsmEvent.GetFsmEvent("CLIMB NEEDLE ATTACK");
            var climbWebEvent = FsmEvent.GetFsmEvent("CLIMB WEB ATTACK");
            var climbSilkBallEvent = FsmEvent.GetFsmEvent("CLIMB SILK BALL ATTACK");

            actions.Add(new SendRandomEventV4
            {
                events = new FsmEvent[] { climbNeedleEvent, climbWebEvent, climbSilkBallEvent },
                weights = new FsmFloat[] { new FsmFloat(1.0f), new FsmFloat(0.8f), new FsmFloat(0.6f) },
                eventMax = new FsmInt[] { new FsmInt(1), new FsmInt(1), new FsmInt(1) },
                missedMax = new FsmInt[] { new FsmInt(2), new FsmInt(2), new FsmInt(3) },
                activeBool = new FsmBool { UseVariable = true, Value = true }
            });

            choiceState.Actions = actions.ToArray();
        }

        private void AddClimbNeedleAttackActions(FsmState needleState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbNeedleAttack") { Value = "ExecuteClimbNeedleAttack" },
                parameters = new FsmVar[0]
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(0.8f),
                finishEvent = FsmEvent.Finished
            });

            needleState.Actions = actions.ToArray();
        }

        private void AddClimbWebAttackActions(FsmState webState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbWebAttack") { Value = "ExecuteClimbWebAttack" },
                parameters = new FsmVar[0]
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(1.0f),
                finishEvent = FsmEvent.Finished
            });

            webState.Actions = actions.ToArray();
        }

        private void AddClimbSilkBallAttackActions(FsmState silkBallState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                fsm = _bossControlFsm,
                variableName = new FsmString("Climb Cast Pending") { Value = "Climb Cast Pending" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbSilkBallAttack") { Value = "ExecuteClimbSilkBallAttack" },
                parameters = new FsmVar[0]
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(2.0f),
                finishEvent = FsmEvent.Finished
            });

            silkBallState.Actions = actions.ToArray();
        }

        private void AddClimbAttackCooldownActions(FsmState cooldownState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new WaitRandom
            {
                timeMin = new FsmFloat(2f),
                timeMax = new FsmFloat(3f),
                finishEvent = FsmEvent.Finished
            });

            cooldownState.Actions = actions.ToArray();
        }

        private void AddClimbAttackTransitions(FsmState choiceState, FsmState needleState,
            FsmState webState, FsmState silkBallState, FsmState cooldownState)
        {
            choiceState.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB NEEDLE ATTACK"), needleState),
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB WEB ATTACK"), webState),
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB SILK BALL ATTACK"), silkBallState)
            };

            SetFinishedTransition(needleState, cooldownState);
            SetFinishedTransition(webState, cooldownState);
            SetFinishedTransition(silkBallState, cooldownState);

            SetFinishedTransition(cooldownState, choiceState);

            Log.Info("爬升阶段攻击转换设置完成");
        }

        private void AddClimbPhaseAttackGlobalTransitions(FsmState climbAttackChoice, FsmState? idleState)
        {
            var globalTransitions = _attackControlFsm!.Fsm.GlobalTransitions.ToList();

            globalTransitions.Add(new FsmTransition
            {
                FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE ATTACK"),
                toState = "Climb Attack Choice",
                toFsmState = climbAttackChoice
            });

            if (idleState != null)
            {
                globalTransitions.Add(new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE END"),
                    toState = "Idle",
                    toFsmState = idleState
                });
            }

            _attackControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
            Log.Info("爬升阶段攻击全局转换添加完成");
        }

        public void ExecuteClimbNeedleAttack()
        {
            Log.Info("执行爬升阶段针攻击（随机Hand）");
            StartCoroutine(ClimbNeedleAttackCoroutine());
        }

        private IEnumerator ClimbNeedleAttackCoroutine()
        {
            int handIndex = UnityEngine.Random.Range(0, 2);
            HandControlBehavior? selectedHand = handIndex == 0 ? handLBehavior : handRBehavior;

            if (selectedHand == null)
            {
                Log.Error($"未找到Hand {handIndex} Behavior");
                yield break;
            }

            Log.Info($"选择Hand {handIndex} ({selectedHand.gameObject.name})进行爬升阶段环绕攻击（削弱版）");

            selectedHand.StartOrbitAttackSequence();

            yield return new WaitForSeconds(2f);

            selectedHand.StartShootSequence();

            Log.Info("爬升阶段环绕攻击完成（单Hand三根针）");
        }

        public void ExecuteClimbWebAttack()
        {
            Log.Info("执行爬升阶段网攻击（双网旋转）");
            StartCoroutine(ClimbWebAttackCoroutine());
        }

        private IEnumerator ClimbWebAttackCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController未找到，无法执行网攻击");
                yield break;
            }

            Vector3 playerPos = hero.transform.position;

            float firstAngle = UnityEngine.Random.Range(-30f, 30f);
            SpawnClimbWebAtAngle(playerPos, firstAngle);
            Log.Info($"生成第一根网，角度: {firstAngle}°");

            yield return new WaitForSeconds(0.5f);

            float secondAngle = firstAngle + 90f;
            SpawnClimbWebAtAngle(playerPos, secondAngle);
            Log.Info($"生成第二根网，角度: {secondAngle}°");

            yield return new WaitForSeconds(1f);
        }

        private void SpawnClimbWebAtAngle(Vector3 playerPos, float angle)
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj == null)
            {
                Log.Warn("未找到AnySilkBossManager");
                return;
            }

            var webManager = managerObj.GetComponent<Managers.SingleWebManager>();
            if (webManager == null)
            {
                Log.Warn("未找到SingleWebManager组件");
                return;
            }

            Vector3 rotation = new Vector3(0f, 0f, angle);

            webManager.SpawnAndAttack(playerPos, rotation, new Vector3(2f, 1f, 1f), 0f, 0.75f);
            Log.Info($"在玩家位置生成网，角度: {angle}°");
        }

        public void ExecuteClimbSilkBallAttack()
        {
            Log.Info("执行爬升阶段丝球攻击（四角生成）");
            StartCoroutine(ClimbSilkBallAttackCoroutine());
        }

        private IEnumerator ClimbSilkBallAttackCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController未找到，无法执行丝球攻击");
                yield break;
            }

            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager未初始化");
                yield break;
            }

            Vector3 playerPos = hero.transform.position;
            float offset = 15f;

            Vector3[] positions = new Vector3[]
            {
                playerPos + new Vector3(-offset, offset, 0f),
                playerPos + new Vector3(-offset, -offset, 0f),
                playerPos + new Vector3(offset, offset, 0f),
                playerPos + new Vector3(offset, -offset, 0f)
            };
            var ballObjects = new List<GameObject>();
            foreach (var pos in positions)
            {
                var behavior = _silkBallManager.SpawnSilkBall(
                    pos,
                    acceleration: 15f,
                    maxSpeed: 20f,
                    chaseTime: 5f,
                    scale: 1f,
                    enableRotation: true,
                    ignoreWall:true
                );
            }

            yield return new WaitForSeconds(0.6f);

            Log.Info("=== 广播 SILK BALL RELEASE 事件，释放爬升阶段丝球 ===");
            EventRegister.SendEvent("SILK BALL RELEASE");

            yield return new WaitForSeconds(1f);
        }
        #endregion
    }
}

