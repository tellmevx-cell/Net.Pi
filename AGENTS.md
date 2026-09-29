# Net.Pi Agent Guidelines

Welcome to the Net.Pi project. All coding assistants working in this repository should adhere to the following conventions:

1. **Architecture & Independence**:
   - `Net.Pi.Ai` handles model abstraction and SSE streaming.
   - `Net.Pi.Core` maintains the deterministic agent loop and state machine.
   - `Net.Pi.Tools` provides safe, isolated tool implementations.
   - `Net.Pi.Tui` renders differential ANSI terminal output.
   - `Net.Pi.Cli` provides the interactive REPL.

2. **Security & Boundary Guarantees**:
   - Never write or edit files outside the workspace root; always enforce `PathGuard`.
   - Never run unbounded commands that could exhaust memory or hang indefinitely.

3. **Code Quality**:
   - Keep C# 10/12/13 idioms clean and idiomatic.
   - Maintain 0 compiler warnings and 0 build errors.
   - Ensure all automated unit tests pass under `dotnet test`.
