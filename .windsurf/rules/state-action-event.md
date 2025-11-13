---
trigger: always_on
---

FSM 项目规则标准化
首先，回复时全部使用简体中文
范围
适用于本项目内所有与 PlayMaker FSM 可视化、状态/事件/跳转、以及 FsmString 等数据结构相关的实现与审查。
术语
FSM 名称: 状态机实例的逻辑名称。
GameObject 名称: 承载该 FSM 的 Unity 对象名。
状态 State: FSM 的节点。
事件 Event: 触发状态跳转的信号。
跳转 Transition: 从状态 A 通过某事件到状态 B 的边。
Actions: 选中状态的行为列表。
FINISHED: PlayMaker 里的结束事件，禁止作为多路通配使用。
数据结构与 API 约定
FsmString 必须显式赋 Value，构造函数仅设置“键/名字”，不含“值”。
    // 错误：只有名字，没有值
    var s1 = new FsmString("StartShootSequence");
    // 正确：名字与值同时设置
    var s2 = new FsmString("StartShootSequence") { Value = "StartShootSequence" };
状态-事件-跳转 约束（强制）
单事件对应单跳转：从状态 A 跳到状态 B，必须通过唯一的一个事件完成。
禁止多事件收敛到同一跳转：不要让多个不同事件都指向同一目标状态 B。
例如检测一个物品是否碰到墙或玩家，不要让碰到墙触发一个事件然后跳转另一个状态B，碰到玩家却触发不同事件但还跳转B，这是不合理的，必须状态和事件对应，不能多个事件收敛到一个状态
对于一个状态来说，进入后会依次按序号执行《所有的瞬发性行为》，然后再执行那些持续性的行为，然后这时会锁住当前状态，所以单纯的把某个行为放到wait后并不能让他延后触发
对于全是瞬发性Action的State，完成后会自动触发默认事件FINISHED,需要注意
如需表达复杂分支，请使用“中间状态”进行显式拆分，保持「一事件一跳转」不变。
新增事件/跳转后的初始化（强制）
新增 Event 或 Transition 后，必须在最后重新初始化引用：
    _attackControlFsm.Fsm.InitData();
    _attackControlFsm.Fsm.InitEvents();
开发工作流建议
明确 FSM 与 GameObject 名称（顶部标签一致）。
设计状态图时按「一事件一跳转」建模；复杂流转用中间状态拆分。
使用 FsmString 时同步赋 Value。
完成新增/修改后执行 InitData() 与 InitEvents()。
自检通过后再提交。
自检清单（提交前逐项勾选）
[ ] 顶部标签中的 FSM 名称、GameObject 名称与实现一致。
[ ] 所有 A→B 的跳转均由单一事件触发，无多事件收敛。
[ ] 未使用 FINISHED 作为多路通配事件。
[ ] 所有 FsmString 同步设置了 Value。
[ ] 变更后调用了 InitData() 与 InitEvents()。
[ ] 右侧 Actions 对应的状态职责清晰、无副作用越界。
常见错误与修正
错误：new FsmString("X") 未赋值。→ 修正：new FsmString("X") { Value = "X" }
错误：从同一状态对同一目标状态注册多个不同事件。→ 修正：保留唯一事件或拆出中间状态。
错误：变更事件/跳转后未重新初始化。→ 修正：调用 InitData() 与 InitEvents()。