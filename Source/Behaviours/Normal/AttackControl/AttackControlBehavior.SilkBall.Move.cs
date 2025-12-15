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
        #region 移动丝球攻击
        private void InitializeSilkBallDashVariables()
        {
            if (_attackControlFsm == null) return;

            _isGeneratingSilkBall = _attackControlFsm.FsmVariables.FindFsmBool("Is Generating Silk Ball");
            if (_isGeneratingSilkBall == null)
            {
                _isGeneratingSilkBall = new FsmBool("Is Generating Silk Ball") { Value = false };
                var bools = _attackControlFsm.FsmVariables.BoolVariables.ToList();
                bools.Add(_isGeneratingSilkBall);
                _attackControlFsm.FsmVariables.BoolVariables = bools.ToArray();
            }

            _totalDistanceTraveled = _attackControlFsm.FsmVariables.FindFsmFloat("Total Distance Traveled");
            if (_totalDistanceTraveled == null)
            {
                _totalDistanceTraveled = new FsmFloat("Total Distance Traveled") { Value = 0f };
                var floats = _attackControlFsm.FsmVariables.FloatVariables.ToList();
                floats.Add(_totalDistanceTraveled);
                _attackControlFsm.FsmVariables.FloatVariables = floats.ToArray();
            }

            _lastBallPosition = _attackControlFsm.FsmVariables.FindFsmVector2("Last Ball Position");
            if (_lastBallPosition == null)
            {
                _lastBallPosition = new FsmVector2("Last Ball Position") { Value = Vector2.zero };
                var vec2s = _attackControlFsm.FsmVariables.Vector2Variables.ToList();
                vec2s.Add(_lastBallPosition);
                _attackControlFsm.FsmVariables.Vector2Variables = vec2s.ToArray();
            }

            var specialAttack = _attackControlFsm.FsmVariables.FindFsmBool("Special Attack");
            if (specialAttack == null)
            {
                specialAttack = new FsmBool("Special Attack") { Value = false };
                var bools = _attackControlFsm.FsmVariables.BoolVariables.ToList();
                bools.Add(specialAttack);
                _attackControlFsm.FsmVariables.BoolVariables = bools.ToArray();
            }
            var p6WebAttack = _attackControlFsm.FsmVariables.FindFsmBool("Do P6 Web Attack");
            if (p6WebAttack == null)
            {
                p6WebAttack = new FsmBool("Do P6 Web Attack") { Value = false };
                var bools = _attackControlFsm.FsmVariables.BoolVariables.ToList();
                bools.Add(p6WebAttack);
                _attackControlFsm.FsmVariables.BoolVariables = bools.ToArray();
            }
            // _laceSlashObj = _attackControlFsm.FsmVariables.FindFsmGameObject("Lace Slash Obj");
            // if (_laceSlashObj == null || _laceSlashObj.Value == null)
            // {
            //     _laceSlashObj = new FsmGameObject("Lace Slash Obj") { Value = laceCircleSlash };
            //     var objects = _attackControlFsm.FsmVariables.GameObjectVariables.ToList();
            //     objects.Add(_laceSlashObj);
            //     _attackControlFsm.FsmVariables.GameObjectVariables = objects.ToArray();
            // }
            _spikeFloorsX = _attackControlFsm.FsmVariables.FindFsmGameObject("Spike Floors X");
            if (_spikeFloorsX == null || _spikeFloorsX.Value == null)
            {
                _spikeFloorsX = new FsmGameObject("Spike Floors X") { Value = null };
                var objects = _attackControlFsm.FsmVariables.GameObjectVariables.ToList();
                objects.Add(_spikeFloorsX);
                _attackControlFsm.FsmVariables.GameObjectVariables = objects.ToArray();
            }
            _attackControlFsm.FsmVariables.Init();
        }

        private FsmState CreateSilkBallDashPrepareState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Dash Prepare", "移动丝球准备：计算路线并触发Boss移动");

            var actions = new List<FsmStateAction>();

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("CalculateAndSetDashRoute") { Value = "CalculateAndSetDashRoute" },
                parameters = new FsmVar[0],
                everyFrame = false
            });
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                fsm = _bossControlFsm,
                variableName = new FsmString("Silk Ball Dash Pending") { Value = "Silk Ball Dash Pending" },
                setValue = new FsmBool(true),
                everyFrame = false
            });
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartGeneratingSilkBall") { Value = "StartGeneratingSilkBall" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(10f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();
            return state;
        }

        private FsmState CreateSilkBallDashEndState()
        {
            var state = CreateState(_attackControlFsm!.Fsm, "Silk Ball Dash End", "移动丝球结束：停止生成");

            var actions = new List<FsmStateAction>();

            // 立即停止生成丝球（在状态进入时立即执行）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopGeneratingSilkBall") { Value = "StopGeneratingSilkBall" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();

            return state;
        }

        private void CheckAndSpawnSilkBall()
        {
            Vector2 currentPos = transform.position;

            if (_lastBallPosition!.Value == Vector2.zero)
            {
                _lastBallPosition.Value = currentPos;
                return;
            }

            float distance = Vector2.Distance(currentPos, _lastBallPosition.Value);
            _totalDistanceTraveled!.Value += distance;
            _lastBallPosition.Value = currentPos;

            if (_totalDistanceTraveled.Value >= 5f)
            {
                SpawnSilkBallAtPosition(currentPos);
                _totalDistanceTraveled.Value = 0f;
            }
        }

        private void SpawnSilkBallAtPosition(Vector2 position)
        {
            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager未初始化，无法生成丝球");
                return;
            }

            Vector3 spawnPos = new Vector3(position.x, position.y, 0f);
            var behavior = _silkBallManager.SpawnSilkBall(spawnPos, 35f, 25f, 8f, 1f, true);

            if (behavior != null)
            {
                StartCoroutine(DelayedReleaseSilkBallForDash(behavior.gameObject));
            }
        }

        private IEnumerator DelayedReleaseSilkBallForDash(GameObject silkBall)
        {
            if (silkBall == null)
            {
                yield break;
            }

            var behavior = silkBall.GetComponent<SilkBallBehavior>();

            float waited = 0f;
            const float maxWait = 0.5f;
            while (behavior != null && !behavior.isPrepared && waited < maxWait)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            var controlFsm = silkBall.LocateMyFSM("Control");
            if (controlFsm != null)
            {
                controlFsm.SendEvent("SILK BALL RELEASE");
            }
        }

        public void CalculateAndSetDashRoute()
        {
            if (_bossControlFsm == null)
            {
                Log.Error("BossControl FSM未初始化，无法设置路线");
                return;
            }

            Vector2 bossPos = transform.position;

            var heroIsFar = _bossControlFsm.FsmVariables.FindFsmBool("Hero Is Far");
            bool isFar = heroIsFar != null && heroIsFar.Value;

            var specialAttackVar = _attackControlFsm?.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            BossZone zone = GetBossZone(bossPos.x);

            Vector3 point0, point1, point2;
            Vector3? pointSpecial = null;

            if (zone == BossZone.Middle)
            {
                bool goLeft = UnityEngine.Random.value > 0.5f;
                point0 = goLeft ? POS_LEFT_UP : POS_RIGHT_UP;
                point1 = goLeft ? POS_RIGHT_UP : POS_LEFT_UP;
                Log.Info($"中区路线: {(goLeft ? "左上→右上" : "右上→左上")}→中下");
            }
            else if (isFar)
            {
                point0 = POS_MIDDLE_UP;
                point1 = (zone == BossZone.Left) ? POS_LEFT_UP : POS_RIGHT_UP;
                Log.Info($"{zone}区+远距离: 中上→{(zone == BossZone.Left ? "左上" : "右上")}→中下");
            }
            else
            {
                if (zone == BossZone.Left)
                {
                    point0 = POS_RIGHT_UP;
                    point1 = POS_LEFT_UP;
                    Log.Info("左区+近距离: 右上→左上→中下");
                }
                else
                {
                    point0 = POS_LEFT_UP;
                    point1 = POS_RIGHT_UP;
                    Log.Info("右区+近距离: 左上→右上→中下");
                }
            }

            point2 = POS_MIDDLE_DOWN;

            if (isPhase2)
            {
                if (zone == BossZone.Left)
                {
                    pointSpecial = POS_RIGHT_DOWN;
                    Log.Info("Phase2模式：Boss在左侧，Special点位 = 右下");
                }
                else if (zone == BossZone.Right)
                {
                    pointSpecial = POS_LEFT_DOWN;
                    Log.Info("Phase2模式：Boss在右侧，Special点位 = 左下");
                }
                else
                {
                    var hero = HeroController.instance;
                    if (hero != null && hero.transform.position.x < bossPos.x)
                    {
                        pointSpecial = POS_RIGHT_DOWN;
                        Log.Info("Phase2模式：Boss在中区，Hero在左侧，Special点位 = 右下");
                    }
                    else
                    {
                        pointSpecial = POS_LEFT_DOWN;
                        Log.Info("Phase2模式：Boss在中区，Hero在右侧或未找到Hero，Special点位 = 左下");
                    }
                }
            }

            if (isPhase2 && pointSpecial.HasValue)
            {
                SetRoutePointSpecial(pointSpecial.Value);
            }
            SetRoutePoint(0, point0);
            SetRoutePoint(1, point1);
            SetRoutePoint(2, point2);

            var bossBehavior = gameObject.GetComponent<BossBehavior>();
            if (bossBehavior != null)
            {
                bossBehavior.UpdateTargetPointPositions(point0, point1, point2, pointSpecial);
            }
            else
            {
                Log.Warn("未找到BossBehavior组件，无法更新隐形目标点位置");
            }

            string routeLog = isPhase2 && pointSpecial.HasValue
                ? $"Special({pointSpecial.Value}) → {point0} → {point1} → {point2}"
                : $"{point0} → {point1} → {point2}";
            Log.Info($"路线已设置: {routeLog}");
        }

        private BossZone GetBossZone(float x)
        {
            if (x < ZONE_LEFT_MAX) return BossZone.Left;
            if (x > ZONE_RIGHT_MIN) return BossZone.Right;
            return BossZone.Middle;
        }

        private void SetRoutePointSpecial(Vector3 value)
        {
            var bossBehavior = gameObject.GetComponent<BossBehavior>();
            if (bossBehavior == null)
            {
                Log.Error("未找到BossBehavior组件，无法设置Special路线点");
                return;
            }

            if (bossBehavior.RoutePointSpecialX != null && bossBehavior.RoutePointSpecialY != null)
            {
                bossBehavior.RoutePointSpecialX.Value = value.x;
                bossBehavior.RoutePointSpecialY.Value = value.y;
                Log.Info($"设置Special路线点: X={value.x}, Y={value.y}");
            }
            else
            {
                Log.Error("RoutePointSpecialX 或 RoutePointSpecialY 为 null");
            }
        }

        private void SetRoutePoint(int index, Vector3 value)
        {
            var bossBehavior = gameObject.GetComponent<BossBehavior>();
            if (bossBehavior == null)
            {
                Log.Error("未找到BossBehavior组件，无法设置路线点");
                return;
            }

            switch (index)
            {
                case 0:
                    if (bossBehavior.RoutePoint0X != null && bossBehavior.RoutePoint0Y != null)
                    {
                        bossBehavior.RoutePoint0X.Value = value.x;
                        bossBehavior.RoutePoint0Y.Value = value.y;
                        Log.Info($"设置路线点 0: X={value.x}, Y={value.y}");
                    }
                    else
                    {
                        Log.Error("RoutePoint0X 或 RoutePoint0Y 为 null");
                    }
                    break;
                case 1:
                    if (bossBehavior.RoutePoint1X != null && bossBehavior.RoutePoint1Y != null)
                    {
                        bossBehavior.RoutePoint1X.Value = value.x;
                        bossBehavior.RoutePoint1Y.Value = value.y;
                        Log.Info($"设置路线点 1: X={value.x}, Y={value.y}");
                    }
                    else
                    {
                        Log.Error("RoutePoint1X 或 RoutePoint1Y 为 null");
                    }
                    break;
                case 2:
                    if (bossBehavior.RoutePoint2X != null && bossBehavior.RoutePoint2Y != null)
                    {
                        bossBehavior.RoutePoint2X.Value = value.x;
                        bossBehavior.RoutePoint2Y.Value = value.y;
                        Log.Info($"设置路线点 2: X={value.x}, Y={value.y}");
                    }
                    else
                    {
                        Log.Error("RoutePoint2X 或 RoutePoint2Y 为 null");
                    }
                    break;
                default:
                    Log.Error($"无效的路线点索引: {index}");
                    break;
            }
        }

        public void StartGeneratingSilkBall()
        {
            if (_isGeneratingSilkBall != null)
            {
                _isGeneratingSilkBall.Value = true;
                _totalDistanceTraveled!.Value = 0f;
                _lastBallPosition!.Value = transform.position;
                Log.Info("开始生成移动丝球");
            }
        }

        public void StopGeneratingSilkBall()
        {
            if (_isGeneratingSilkBall != null)
            {
                _isGeneratingSilkBall.Value = false;
                if (_totalDistanceTraveled != null)
                {
                    _totalDistanceTraveled.Value = 0f;
                }
                if (_lastBallPosition != null)
                {
                    _lastBallPosition.Value = Vector2.zero;
                }
                Log.Info("停止生成移动丝球并重置变量");
            }
        }
        #endregion
    }
}

