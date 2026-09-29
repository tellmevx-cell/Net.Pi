# Net.Pi

Lightweight, high-performance, deterministic AI Agent toolkit and coding agent CLI for **.NET 10 / 9 / 8**, ported from the architecture of **Earendil Works Pi (`earendil-works/pi`)**.

---

## 架构体系

```text
Net.Pi/
├── src/
│   ├── Net.Pi.Ai/       # 统一的多模型流式客户端抽象 (OpenAI / DeepSeek / Gemini / Ollama / Mock)
│   ├── Net.Pi.Core/     # 确定性 Agent 循环 (AgentLoop)、事件状态机与上下文压缩管理器
│   ├── Net.Pi.Tools/    # 生产级安全编码核心工具集与 PathGuard 安全防护网
│   ├── Net.Pi.Tui/      # 终端流式渲染引擎 (ANSI 颜色、工具调用可视化、状态显示)
│   └── Net.Pi.Cli/      # 交互式 Coding Agent CLI 控制台 (支持 Ctrl+C 取消、/reset 与 /clear)
└── tests/
    └── Net.Pi.Tests/    # 17 项全覆盖自动化测试套件 (包含 P0 异常分支、取消、边界防御与网络重试)
```

---

## 核心特性 (v0.2 工业级强化版)

1. **统一流式 LLM 客户端 (`Net.Pi.Ai`)**
   - 兼容 OpenAI、DeepSeek、Gemini、Qwen、Ollama、vLLM 等全部标准接口。
   - **抗网络抖动**：支持指数退避重试（3 次自动重试）与 60s 静默读取超时保护。
   - **规范 SSE 事件流解析器**：支持多行 data 累积、`: ping` 注释忽略以及严格的 finish_reason 保存。
   - 内置离线可用的 `MockLlmClient`，可模拟网络故障与各种复杂交互流。

2. **确定性 Agent 循环与状态机 (`Net.Pi.Core`)**
   - **异常安全保证**：全面包裹异步迭代器，401/断网/超时统一触发 `AgentErrorOccurred` 与 `AgentRunStatus.Error`，彻底终结进程未捕获崩溃。
   - **上下文自适应压缩 (`ContextCompactor`)**：长会话自动折叠陈旧大工具输出、提炼早期多轮对话摘要，告别 Provider 上下文超限。
   - **精准状态分类**：明确区分 `Completed`、`MaxTurnsReached`、`Cancelled` 和 `Error`。

3. **内置安全与编码核心工具集 (`Net.Pi.Tools`)**
   - **`PathGuard` 路径沙箱**：严格校验相对/绝对路径，禁止 `../` 路径穿越逃逸工作区。
   - **`grep`**：正则全文检索，内置 5 秒超时保护彻底免疫 ReDoS 攻击。
   - **`glob`**：快速文件匹配，自动排除 `bin`、`obj`、`.git`、`node_modules` 等非源码目录。
   - **`edit_file`**：精确查找替换，防空串死循环与多重匹配歧义防护。
   - **`execute_command`**：Windows 预置 UTF-8（`chcp 65001`）避免终端中文乱码，输出带 2000 行/512KB 上限保护防 OOM。
   - **`web_search` & `web_fetch`**：原生网页检索与 Markdown 内容提取。
   - **`agent_browser`**：无头浏览器渲染与截图自动化。
   - **`read_skill`**：按需动态加载 `SKILL.md` 专业技能指导。

4. **无闪烁 TUI 与交互控制 (`Net.Pi.Tui` & `Net.Pi.Cli`)**
   - 支持 **Ctrl+C 优雅取消**：仅终止当前思考或工具执行，不退出终端会话。
   - 支持 `/reset`（清空历史启动新会话）、`/clear`（清屏保留历史）、`/history`（查看 Token 统计）。

---

## 快速上手

### 1. 运行自动化测试套件
```bash
cd C:\monica\code\ai-project\Net.Pi
dotnet test
```

### 2. 本地模拟模式验证
```bash
dotnet run --project src/Net.Pi.Cli/Net.Pi.Cli.csproj -- --mock "Please list files in the current dir"
```

### 3. 连接真实大模型运行

配置环境变量：
```cmd
set OPENAI_API_KEY=your-api-key
set OPENAI_BASE_URL=http://10.16.10.122:8045/v1
set OPENAI_MODEL=gemini-3.7-flash

dotnet run --project src/Net.Pi.Cli/Net.Pi.Cli.csproj
```
或直接使用已配置好环境变量的 `run-ccs.bat`。
