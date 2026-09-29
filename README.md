# Net.Pi

Lightweight, high-performance, deterministic AI Agent toolkit and coding agent CLI for **.NET 10 / 9 / 8**, ported from the architecture of **Earendil Works Pi (`earendil-works/pi`)**.

---

## 架构体系

```text
Net.Pi/
├── src/
│   ├── Net.Pi.Ai/       # 统一的多模型流式客户端抽象 (OpenAI / DeepSeek / Gemini / Ollama / Mock)
│   ├── Net.Pi.Core/     # 确定性 Agent 循环、会话持久化 (SessionStore) 与上下文压缩
│   ├── Net.Pi.Tools/    # 生产级安全编码核心工具集与 PathGuard 符号链接安全防护网
│   ├── Net.Pi.Tui/      # 终端流式渲染引擎 (ANSI 颜色、Token/成本统计可视化、状态显示)
│   └── Net.Pi.Cli/      # 交互式 Coding Agent CLI (支持 --resume、--ask 审批门、Ctrl+C 取消)
└── tests/
    └── Net.Pi.Tests/    # 21 项全覆盖自动化测试套件 (包含 P0 异常、会话恢复、权限门与网络重试)
```

---

## 核心特性 (v0.3 完整对标版)

1. **统一流式 LLM 客户端 (`Net.Pi.Ai`)**
   - 兼容 OpenAI、DeepSeek、Gemini、Qwen、Ollama、vLLM 等全部标准接口。
   - **抗网络抖动**：支持指数退避重试（3 次自动重试）与 60s 静默读取超时保护。
   - **规范 SSE 事件流解析器**：支持多行 data 累积、`: ping` 注释忽略以及严格的 finish_reason 保存。
   - 内置离线可用的 `MockLlmClient`，可模拟网络故障与各种复杂交互流。

2. **确定性 Agent 循环与状态机 (`Net.Pi.Core`)**
   - **会话持久化与恢复 (`SessionStore`)**：会话自动保存至 `.net-pi/sessions/`，支持 `--resume` 断点续接。
   - **项目规约探测 (`AGENTS.md`)**：自动遍历项目及上级目录，发现 `AGENTS.md` / `CLAUDE.md` 自动注入系统上下文。
   - **异常安全保证**：全面包裹异步迭代器，401/断网/超时统一触发 `AgentErrorOccurred` 与 `AgentRunStatus.Error`，彻底杜绝未捕获异常崩溃。
   - **上下文自适应压缩 (`ContextCompactor`)**：长会话自动折叠陈旧大工具输出、提炼早期多轮对话摘要，告别 Provider 上下文超限。
   - **精准状态分类**：明确区分 `Completed`、`MaxTurnsReached`、`Cancelled` 和 `Error`。

3. **内置安全与编码核心工具集 (`Net.Pi.Tools`)**
   - **`PathGuard` 软链接防护**：严格校验相对/绝对路径，通过 `ResolveLinkTarget` 真实路径解构，彻底杜绝符号链接与 `../` 路径穿越逃逸。
   - **`execute_command` 审批门**：支持 `--ask` 参数开启交互式确认门（`y/N`），拒绝不受信任的命令执行；Windows 自动预注入 `chcp 65001` 保证 UTF-8 输出，2000 行/512KB 硬截断防 OOM。
   - **`grep`**：正则全文检索，内置 5 秒超时保护彻底免疫 ReDoS 攻击。
   - **`glob`**：快速文件匹配，自动排除 `bin`、`obj`、`.git`、`node_modules` 等非源码目录。
   - **`edit_file`**：精确查找替换，防空串死循环与多重匹配歧义防护。
   - **`web_search` & `web_fetch`**：原生网页检索与 Markdown 内容提取。
   - **`agent_browser`**：无头浏览器渲染与截图自动化。
   - **`read_skill`**：按需动态加载 `SKILL.md` 专业技能指导。

4. **无闪烁 TUI 与交互控制 (`Net.Pi.Tui` & `Net.Pi.Cli`)**
   - **Token 与成本实时核算**：每轮输出自动打印耗时、Token 构成（Prompt / Completion / Reasoning）及估算费用（USD）。
   - **Ctrl+C 优雅取消**：仅终止当前思考或工具执行，不退出终端会话。
   - **命令行扩展指令**：支持 `/resume`、`/sessions`（列出会话）、`/reset`（清空历史）、`/clear`（清屏）、`/history`（查看 Token 统计）。

5. **单文件免安装独立打包 (`publish-single-file.bat`)**
   - 原生打包为单文件 `Net.Pi.Cli.exe`，目标机器无需安装任何 .NET SDK 或 Runtime。

---

## 快速上手

### 1. 运行自动化测试套件 (21 项全部通过)
```bash
cd C:\monica\code\ai-project\Net.Pi
dotnet test
```

### 2. 交互式启动与会话恢复
```bash
# 新会话启动
run-ccs.bat

# 断点恢复最近会话
run-ccs.bat --resume

# 开启危险命令人工审批门
run-ccs.bat --ask
```

### 3. 一键打包为独立免安装二进制
```cmd
publish-single-file.bat win-x64
```
产物将输出至 `dist/win-x64/Net.Pi.Cli.exe`。
