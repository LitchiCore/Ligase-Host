# Ligase Host 仓库协作规则

本文件适用于整个 Host 仓库。

## 从实时状态开始

- 每个阶段开始前，必须读取本文件并检查当前分支、`git status`、完整 diff 与所有
  未跟踪文件。仓库可能由多个 Host 任务共享；必须保留 allowlist 外的修改。
- `src_assets/windows/assets/shaders/directx/` 下现存的 DirectX shader 删除不属于
  其他任务范围。不得恢复、暂存、格式化或把它们吸收到新提交。
- 构建产物、临时文件、源码快照和 cache 必须位于批准的 D 盘开发根目录，不得在
  系统盘重新建立物理 build/cache 状态。

## 安装产品与系统 mutation

- 已安装产品使用 structured layout。根目录 launcher 是稳定产品入口；
  Desktop、Core、Tools 与 Deployment 保持各自 owned 子目录。
- 不得用仓库二进制、Debug 输出、改变 working directory 或手工复制依赖掩盖
  installed-product 缺陷。
- 启动或替换已安装产品、运行 installer/UAC、修改 firewall、安装或删除 driver、
  修改 certificate 都需要明确授权。只读检查不授予 mutation 权限。
- Installer automation 必须使用仓库内 typed invocation seam 与 native argv
  harness。不得通过扁平的 `Start-Process -ArgumentList` 数组传递带空格路径，也
  不得用字符串拼接重建 argv。
- 交互式卸载和安装默认由用户手动执行；除非明确授权，agent 只做 preflight 与
  readback。

## 权威与文档边界

- Installer、lifecycle、protocol、firewall 与跨客户端语义必须链接
  `docs/ligase-host/` 下的 canonical owner 文档。不得在第二份文档复制 script、
  wire schema、route matrix、secret 或 security rule。
- 协调根存在 `AUTHORITY.md` 时，它是 Android、Host、Layouts Web 的 owner 与
  字段位置索引。跨仓库任务开始前必须读取；发现路径过期或 ownership 冲突时报告
  协调任务。该文件由协调任务统一维护，仓库任务不得并发修改。它不替代 Host 的
  canonical 文档、machine JSON、route 实现、测试或 typed model。

## 跨仓库协作流程

- 领域 owner 提交审议前，必须形成精确 allowlist、稳定文件大小与 SHA-256、
  machine validation 结果及明确排除项。审议资产保持未提交并标记为 `REVIEW`；
  发布新快照即使全部旧 SHA 失效。
- 每个受影响消费端都必须针对同一固定快照独立执行只读复核，并明确返回
  `ACCEPT` 或 `NEEDS_REVISION`。沉默、旧版 ACCEPT、build 通过或协调摘要都不
  构成接受。
- 任一端返回 `NEEDS_REVISION` 后，快照退回 owner。owner 只能修订审议资产，
  发布全新 SHA，并重新走所有必要审议。所有必要 reviewer 接受前，禁止生产实现、
  stage authority、构建候选或兼容兜底。
- 所有必要 reviewer 均 ACCEPT 后，仍须等待协调任务明确授权，才能精确 stage、
  commit、push machine authority 并回读 remote SHA。生产实现必须获得单独范围，
  按可编译的依赖顺序分块提交。
- 前后端可直接沟通 typed seam 和阻塞，但 review verdict、授权需求、commit SHA、
  runtime mutation 与最终证据都必须同时回报协调任务。`PEER_ALREADY_NOTIFIED`
  只表示已通知，不代表获得更大授权。
- 共享 checkout 中不得 stage、revert、format 或修复 peer WIP。Build、产品、
  installer 和 runtime 窗口必须显式预约并及时释放。
- 跨仓库变更必须留在各自 owner 仓库并独立提交。消费者依赖新 authority 或 typed
  API 时，provider commit 必须先到达。报告中必须区分 `REVIEW`、`FROZEN`、
  `TRANSITIONAL`、已实现、已打包、已安装、已运行验证与阻塞状态。

## Git、文档与回报

- 每个任务 final 与 commit handoff 必须声明 `DOC_IMPACT=UPDATED|NONE` 并说明
  理由。用户行为/UI flow、typed owner/依赖、installation layout/launcher/
  bootstrap、protocol/security/error/session、permission/UAC/firewall/driver
  或真实验收步骤发生变化时，必须在同一提交更新 owner 文档。纯机械修改或仅强化
  既有行为的测试可以使用 `NONE`。
- 文档必须链接 machine JSON、schema 与 protocol authority，不得复制第二份权威。
  不得把 planned 或 `REAL_UI_PENDING` 工作描述为已实现/已通过，也不得在稳定文档
  写入临时 commit SHA 或测试计数。
- 只能精确 stage 已批准路径。禁止 `git add .`，禁止暂存无关工作，commit 前必须
  复核 staged diff。
- 必须分开报告四层证据：源码/单元/build、packaged payload、installed-product UI、
  真实 pairing/stream session。前一层不能替代后一层。
- commit/push 前运行与风险相称的测试、`git diff --check`、allowlist 复核及
  secret/绝对路径扫描。push 后回读 remote SHA，并报告本任务 scope 是否 clean。
- 每个阶段按结构化格式回报：阶段/结论、验证证据、精确文件范围、剩余阻塞、
  peer/user 动作、commit/remote readback、`DOC_IMPACT`，以及实际发生或未发生的
  runtime/system mutation。
