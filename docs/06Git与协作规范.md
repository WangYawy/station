# 视音频数据采集站与监控管理平台 — Git 与协作规范

> 版本：0.1.0（基线）　配套：《开发规范》《CI 与发布规范》《日常开发流程与检查清单》

## 一、仓库与分支模型

### 长期分支

- **`main` 是唯一长期分支**，代表始终可构建、可交付的状态；
- `main` 受保护：合入前 CI 必须通过；禁止 `force push` 到 `main`。

### 功能分支

| 场景 | 分支名 | 示例 |
| :--- | :--- | :--- |
| 里程碑功能 | `feature/M##-kebab-desc` | `feature/M49-platform-alert` |
| 缺陷修复 | `fix/M##-kebab-desc` | `fix/M48-mtp-copy` |
| 文档 | `docs/...` | `docs/git-standard` |
| 杂项/维护 | `chore/...` | `chore/update-ci` |

- 单人/小团队现状下，小改动且本地验证充分时允许直接提交 `main`；
- **多人协作时必须走分支 + Pull Request**，禁止直接推 `main`。

## 二、提交信息规范

### 格式

```text
type(scope): M## 简述（补充说明）
```

- `type`：`feat` / `fix` / `docs` / `perf` / `refactor` / `test` / `chore` / `style` / `build` / `ci` / `revert`；
- `scope`：受影响端或模块，常用：`platform`、`desktop`、`web`（单机版内置 Web）、`platform-web`、`shared`、`security`、`license`、`export`、`sync`、`collect`、`backup`、`deploy`、`ops`、`recorder`、`alerts`、`upload`、`config`、`auth`、`rbac`、`e2e` 等；
- 跨端改动可合并写：`feat(platform+desktop): ...`；
- `M##`：里程碑编号，一个提交对应一个里程碑；合并里程碑可写 `M39+M40`；
- 简述 ≤ 72 字符，中文描述允许，与仓库历史保持一致。

### 历史示例（照此风格）

```text
feat(platform+desktop): M48 SignalR 实时推送 + MTP 采集源
feat(export): M47 台账/报表导出 xlsx/PDF（三格式 + 前端下拉 + 平台连接池泄漏修复）
feat(sync): M46 用户域配置全量下发（组织/用户/账号/角色/记录仪白名单）
perf(platform-web): M27 前端代码分割 + ECharts 按需引入
docs(ops): M36 部署与运维手册
```

### 规则

1. **一条提交一个逻辑变更**，禁止混入无关重构、格式调整或半成品代码；
2. 正文说明"为什么"，必要时附 spike / 里程碑文档路径；
3. 提交前自查 `git status`、`git diff`，不提交无关文件；
4. 不提交：密钥、`.env`、`publish/`、`bin/`、`obj/`、`node_modules/`、本地库文件（`*.db` 等）；
5. 不提交未验证的代码（验证标准见《开发规范》第九章）。

## 三、里程碑（M##）机制

- 每个功能点分配**递增的 M 编号**（当前最新为 M48），新功能从 M49 开始；
- 流程：`docs/milestones/M##-xxx.md`（方案 + 验证表格）→ `spikes/Station.Spike.Xxx`（高风险验证）→ 正式实现 → 提交（引用 M##）→ 里程碑文档标记 ✅；
- 提交信息必须引用 M##，保证代码可回溯到需求与验证记录；
- 里程碑文档固定记录：目标、方案要点、验证场景/结果表、后续事项。

## 四、Pull Request 规范（多人协作时）

- PR 标题遵循提交信息格式；描述使用模板：

  ```text
  背景：<为什么要做>
  改动：<改了什么>
  验证：<本地构建/测试/联调结果>
  关联：M##、spike 文档链接
  ```

- Review 关注点：分层依赖是否被破坏、Shared 契约变更影响面、安全（验签/脱敏/审计）、三库兼容、文档是否同步；
- 合入方式：**squash merge** 保持 `main` 线性整洁；合入前先 rebase `main` 并确认 CI 绿；
- 合入后立即确认对应 workflow 通过，失败则当日内修复或回滚。

## 五、版本与发布

- 版本号统一维护在 `Directory.Build.props` 的 `<Version>`（当前 `0.1.0`）；
- 桌面端、平台后端、前端 `package.json` 的版本号保持一致；
- 每次发布递增版本并打标签：`v0.x.y`（标签注明覆盖的里程碑范围）；
- 发布产物不入库（`publish/` 已 gitignore），交付物按《CI 与发布规范》产出。

## 六、敏感信息与忽略规则

- 仓库 `.gitignore` 已覆盖：`bin/`、`obj/`、`node_modules/`、`dist/`、`logs/`、`*.db*`、`publish/`、`archive/`、IDE/OS 文件；
- 新增配置模板只提交 `.env.example` 与占位值，真实值走部署环境变量；
- 发现敏感信息被误提交：**立即轮换密钥**，清理历史需 owner 批准后再执行 `filter-repo` 等重写操作；
- 不得以"先提交、后删除"的方式处理密钥。

## 七、回滚与恢复

| 场景 | 操作 |
| :--- | :--- |
| 已推送到 `main` 的错误 | `git revert <sha>`（保留历史），随后发布回滚配置/版本 |
| 本地未推送的错误 | `git reset --soft` 保留改动重做；确认丢弃才用 `--hard` |
| 误提交的敏感文件 | 立即轮换密钥；历史清理需 owner 批准 |

- 禁止 `git reset --hard` / `git checkout --` 丢弃他人或已推送的改动；
- 发布回滚顺序：先回滚部署配置/版本，再回滚代码，保证可恢复。

## 八、常用命令速查

```powershell
# 建分支
git switch -c feature/M49-platform-alert

# 自查
git status
git diff --check          # 空白/行尾错误

# 提交（信息按规范）
git add <files>
git commit

# 合入 main（squash 保持线性）
git switch main
git pull --rebase
git merge --squash feature/M49-platform-alert

# 打标签
git tag v0.2.0
git push origin main --tags
```

## 九、日常使用场景 SOP

> 当前仓库尚未配置远程仓库（`git remote -v` 为空）；以下涉及 `git push` / `git pull` 的场景，
> 需先与管理员确认远程地址后执行 `git remote add origin <仓库地址>`，再按步骤操作。

### 场景 1：开始新任务

```powershell
git switch main
git pull --rebase        # 多人协作时先同步；无远程时可跳过
git status               # 确认工作区干净；有未提交改动先按场景 4 处理
git switch -c feature/M49-platform-alert   # 分支名按"一、分支模型"命名
```

✅ 预期：已切到新分支，工作区无残留改动。

### 场景 2：完成改动后提交（标准动作）

```powershell
git status                # 看改动清单
git diff                  # 看未暂存改动内容
git diff --check          # 空白/行尾错误检查
git add docs/06Git与协作规范.md scripts/package-windows.ps1   # 只加本次相关文件
git status                # 复核暂存区，确认没有无关文件
git commit                # 提交信息按"二、提交信息规范"
```

提交信息示例：

```text
docs(ops): M49 Git 日常使用场景 SOP
```

✅ 预期：`git log -1` 显示本次提交；暂存区只含本任务文件。

### 场景 3：提交后要补充文件或改信息（未推送时）

```powershell
git add <漏掉的文件>
git commit --amend --no-edit        # 补进上一次提交，不改信息
git commit --amend                  # 或顺便修改提交信息
```

> 只用于**未推送**的提交；已推送的提交禁止 amend（改写历史），改用新提交或 `revert`。

### 场景 4：开发到一半要切换任务

```powershell
git stash push -m "M49 告警列表进行中"   # 暂存未提交改动
git switch main                          # 去处理急事
# 处理完回来
git switch feature/M49-platform-alert
git stash list
git stash pop                            # 恢复改动（有冲突按场景 7 解决）
```

> `stash pop` 后立即检查恢复结果；不要连续多次 pop，避免现场丢失。

### 场景 5：从远端同步最新代码

```powershell
git switch main
git pull --rebase        # rebase 保持线性历史
git switch feature/M49-platform-alert
git rebase main          # 把功能分支重放到最新 main（冲突按场景 7 解决）
```

> 提交已被推送且与他人共享时，不要 rebase 改写，改用 `git merge`。

### 场景 6：推送 / 创建 PR / 合入

```powershell
git push -u origin feature/M49-platform-alert     # 首次推送并建立关联
# 多人协作：在 GitLab/GitHub 建 PR，CI 绿后 squash merge
git switch main
git pull --rebase
git merge --squash feature/M49-platform-alert
git commit -m "feat(platform): M49 告警列表"
git push origin main
```

单人小改动且本地验证充分时，允许直接提交 `main`（见"一、分支模型"）。

### 场景 7：解决冲突

```powershell
git rebase main          # 或 git merge，出现冲突时
git status               # 查看冲突文件（Unmerged paths）
# 编辑冲突文件：保留 <<<<<<< ======= >>>>>>> 标记之间正确内容，然后删除标记
git add <冲突文件>       # 标记已解决
git rebase --continue    # 或 git merge --continue
```

> 拿不准保留哪边时，先与相关改动人确认，禁止"两边都留"式乱合并。

### 场景 8：改错了怎么撤销（按状态选）

| 状态 | 命令 | 影响 |
| :--- | :--- | :--- |
| 未暂存（改了还没 add） | `git restore <file>` | 丢弃工作区改动，回到暂存区/HEAD |
| 已暂存（add 了没提交） | `git restore --staged <file>` | 取消暂存，文件改动保留 |
| 已提交未推送 | `git reset --soft HEAD~1` | 撤销提交，改动留在暂存区可重做 |
| 已推送 | `git revert <sha>` | 生成反向提交，不动历史 |
| 误提交敏感信息 | 立即轮换密钥，历史清理需 owner 批准 | 见"六、敏感信息" |

> 禁止 `git reset --hard` / `git checkout --` 丢弃他人或已推送的改动。

### 场景 9：误删文件 / 误丢提交的恢复

```powershell
git restore <误删文件>            # 从 HEAD 恢复
git reflog                         # 找回被 reset/误删的提交（列出操作历史）
git cherry-pick <丢失提交的sha>    # 在 reflog 中找到后捡回
```

### 场景 10：发布打标签（触发 CI Release）

```powershell
git switch main
git pull --rebase
# 1) 版本号统一递增：Directory.Build.props 的 <Version> 与前端 package.json
# 2) 提交版本变更
git add Directory.Build.props station-web/package.json station-platform-web/package.json
git commit -m "chore(version): M49 v0.2.0 版本号递增"
# 3) 打标签并推送（触发 release.yml：生成桌面端安装包并发布 GitHub Release）
git tag v0.2.0
git push origin main --tags
```

✅ 预期：Actions 中 `Desktop Release` 工作流自动执行；发布后核对 Release 附件（MSI + 两个 DEB）。

### 场景 11：每天收工前检查

```powershell
git status                 # 无意外文件
git diff --check           # 无空白错误
git log --oneline -3       # 今日提交都在
```

- 未提交但有价值的改动：要么按场景 2 提交，要么 `stash` 并记录 TODO，禁止裸留在工作区。
