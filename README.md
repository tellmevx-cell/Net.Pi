# Net.Pi

Lightweight, high-performance, deterministic AI Agent toolkit and coding agent CLI for **.NET 10 / 9 / 8**, ported from the architecture of **Earendil Works Pi (`earendil-works/pi`)**.

---

## 架构体系

```text
Net.Pi/
├── src/
│   ├── Net.Pi.Ai/       # 统一的多模型 LLM 客户端抽象 (OpenAI / DeepSeek / Ollama / Mock)
│   ├── Net.Pi.Core/     # 确定性 Agent 循环 (AgentLoop)、事件流与状态机
│   ├── Net.Pi.Tools/    # 开箱即用的编码核心工具 (read_file, write_file, edit_file, list_dir, execute_command)
│   ├── Net.Pi.Tui/      # 终端流式渲染引擎 (ANSI 颜色、工具调用可视化、状态显示)
│   └── Net.Pi.Cli/      # 交互式 Coding Agent CLI 控制台
└── tests/
    └── Net.Pi.Tests/    # 单元测试与端到端回归验证
```

---

## 核心特性

1. **统一流式 LLM 客户端 (`Net.Pi.Ai`)**
   - 兼容 OpenAI、DeepSeek、Qwen、Ollama、vLLM 等全部兼容 OpenAI 接口的模型。
   - 采用低开销 SSE（Server-Sent Events）流式处理，零等待。
   - 内置离线可用的 `MockLlmClient`，无需真实 API Key 即可进行自动化单元测试。

2. **极简确定性 Agent 循环 (`Net.Pi.Core`)**
   - 告别黑盒 Agent 抽象，循环生命周期透明可控（`IAsyncEnumerable<AgentEvent>`）。
   - 完整支持多轮对话自动推进与 Tool-Calling 递归执行。

3. **内置安全与编码核心工具集 (`Net.Pi.Tools`)**
   - `glob`：快速文件模式匹配（如 `**/*.cs`），自动排除构建和缓存目录（`bin`、`obj`、`.git`、`node_modules` 等）。
   - `grep`：基于正则表达式的内容全文检索，支持带行号内容展示与纯匹配文件列表。
   - `read_file`：支持带行号切片（offset/limit）查看大文件。
   - `write_file`：自动创建缺失父目录，安全写盘。
   - `edit_file`：精准字符串替换，支持唯一性保护与批量替换。
   - `list_dir`：分目录与文件清晰列举工作区内容。
   - `execute_command`：跨平台系统命令执行（Windows `cmd.exe` / Linux `bash`），带超时与取消保护。

4. **无闪烁 TUI 交互 (`Net.Pi.Tui`)**
   - 流式响应打字机效果。
   - 清晰高亮工具调用参数与执行状态。

---

## 快速上手

### 1. 运行内置模拟模式（无需 API Key）
```bash
cd C:\monica\code\ai-project\Net.Pi
dotnet run --project src/Net.Pi.Cli/Net.Pi.Cli.csproj -- --mock "Please list files in the current dir"
```

### 2. 连接真实大模型运行

配置环境变量：
```cmd
set OPENAI_API_KEY=your-api-key
set OPENAI_BASE_URL=https://api.openai.com/v1
set OPENAI_MODEL=gpt-4o-mini
```
或使用国内 DeepSeek：
```cmd
set OPENAI_API_KEY=your-deepseek-key
set OPENAI_BASE_URL=https://api.deepseek.com/v1
set OPENAI_MODEL=deepseek-chat
```

启动交互式终端：
```bash
dotnet run --project src/Net.Pi.Cli/Net.Pi.Cli.csproj
```

### 3. 运行自动化测试
```bash
dotnet test
```

---

## 在你自己的项目中使用 Net.Pi

引入 `Net.Pi.Core` 和 `Net.Pi.Ai` 即可快速装配自己的 Agent：

```csharp
using Net.Pi.Ai;
using Net.Pi.Core;
using Net.Pi.Tools;

// 1. 初始化 LLM
var llm = new OpenAiCompatibleClient("your-api-key", "https://api.deepseek.com/v1", "deepseek-chat");

// 2. 装配工具
var tools = new ITool[] {
    new ReadFileTool(),
    new WriteFileTool()
};

// 3. 启动 Agent 循环
var agent = new AgentLoop(llm, tools, new AgentLoopOptions {
    SystemPrompt = "你是一个专业的 .NET 架构助理。"
});

await foreach (var evt in agent.RunAsync("分析当前代码仓库"))
{
    // 处理或展示事件
}
```
