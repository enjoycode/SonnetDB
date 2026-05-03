# GitHub Milestone / Label 规范（SonnetDB）

本文档定义 SonnetDB 仓库的 Milestone 与 Label 使用规范，用于统一 Issue 拆解、优先级和交付节奏。

## 1. Milestone 规范

### 1.1 命名方式

- 主线里程碑：Milestone N - 主题
- 版本里程碑：v主.次.修订（例如 v1.1.0）

### 1.2 当前建议里程碑

- Milestone 16 - Copilot 产品化升级
- Milestone 17 - 生态与连接器
- Milestone 18 - VS Code 扩展

### 1.3 使用规则

- 每个 Issue 至少挂一个 Milestone。
- 复杂需求拆分为多个 Task Issue，再由一个 Feature Issue 汇总。
- 里程碑关闭前需完成验收标准并清理阻塞项。

## 2. Label 规范

### 2.1 最小集合

每个 Issue 建议至少包含以下三类标签：

- type:*
- prio:*
- status:*

### 2.2 标签字典

| 分组 | Label | 颜色 | 说明 |
|------|-------|------|------|
| Type | type:bug | d73a4a | 缺陷 |
| Type | type:feature | 0e8a16 | 新功能 |
| Type | type:task | 1d76db | 工程任务 |
| Type | type:docs | 5319e7 | 文档改动 |
| Type | type:perf | fbca04 | 性能优化 |
| Type | type:test | c5def5 | 测试相关 |
| Area | area:core | 0052cc | 核心引擎 |
| Area | area:sql | 006b75 | SQL 词法/语法/执行 |
| Area | area:server | 1f883d | HTTP 服务端 |
| Area | area:adonet | 0b7285 | ADO.NET 提供程序 |
| Area | area:web-admin | bfd4f2 | Web 管理后台 |
| Area | area:copilot | 5319e7 | Copilot 相关 |
| Area | area:connector | c2e0c6 | 各语言连接器 |
| Area | area:docs | d4c5f9 | 文档 |
| Priority | prio:P0 | b60205 | 阻断 |
| Priority | prio:P1 | d93f0b | 高 |
| Priority | prio:P2 | fbca04 | 中 |
| Priority | prio:P3 | 0e8a16 | 低 |
| Status | status:triage | ededed | 待分诊 |
| Status | status:ready | c2e0c6 | 可开工 |
| Status | status:in-progress | 1d76db | 进行中 |
| Status | status:blocked | d73a4a | 阻塞 |
| Status | status:review | 5319e7 | 评审中 |
| Status | status:done | 0e8a16 | 已完成 |
| Meta | good first issue | 7057ff | 适合首次贡献 |
| Meta | needs:discussion | f9d0c4 | 需要讨论 |
| Meta | breaking-change | b60205 | 破坏性变更 |

## 3. Issue 生命周期

1. 新建：status:triage + type + prio。
2. 分诊：补充 area 与 milestone，改为 status:ready。
3. 开发：改为 status:in-progress。
4. 提交 PR：改为 status:review，PR 描述关联 Issue。
5. 合并后：改为 status:done 并关闭。

## 4. 实施建议

- 每周清理超过 7 天未分诊的 status:triage。
- 每周复查 status:blocked 并记录阻塞原因。
- 每个 Milestone 收官时复盘标签是否过度膨胀，必要时收敛。
