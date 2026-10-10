# Architecture

(Component, Docker, ER, and chat-turn diagrams are added as the phases that introduce them land.)

## Spike findings (plan task 1.2, run 2026-10-10)

Throwaway console app against a local Ollama 0.40.2, `Microsoft.Extensions.AI` 10.10.0, `OllamaSharp` 5.5.0, two tools (`search_orders`, `propose_order_status_change`) through `ChatClientBuilder(...).UseFunctionInvocation()`.

1. **Model and image.** Tag `qwen3.5:4b` (id `d8b0f5e9760c`, 3.3 GB). Pin the Docker image `ollama/ollama:0.40.2` (tag verified to exist). The model supports thinking and defaults to it (`/api/show` reports `thinking.default = true`), so `think = false` must be sent explicitly.
2. **Tool selection: 10/10.** Right tool for "pending orders", "today's orders", "John's Les Paul order last week", "order 42 is dispatched", "cancel order 43", "which orders are dispatched?", "orders from Mary", "mark order 44 as delivered", "any cancelled orders?", "orders placed yesterday". Gate passed; model choice stands.
3. **`think = false`.** `ChatOptions.AdditionalProperties["think"] = false` works. No `<think>` text appeared in any reply. `ThinkStripper` stays as a defensive fallback.
4. **Dates.** With today's date and weekday in the system prompt, the model passed `yyyy-MM-dd` (e.g. "last week" became `2026-10-03` to `2026-10-10`, "yesterday" became `2026-10-09`). Note "last week" came back as a rolling 7 days, not the Monday-based week; the tool description for date ranges and the prompt must say which one is meant.
5. **Round limit (`MaximumIterationsPerRequest = 5`).** Does not throw and does not return pending tool calls. It returns normally with a final text-only message (11 messages in the test). Detect the limit by counting rounds in `ToolResultLog` (P7).
6. **Errors.** Model not pulled: `HttpRequestException`, "Response status code does not indicate success: 404 (Not Found)". Ollama down: `HttpRequestException`, "No connection could be made because the target machine actively refused it". Both are `HttpRequestException`, so tell them apart by `StatusCode == 404` (D21) versus no status code (unreachable, FR-C13). Timeout is not covered here; use the `CancellationToken` and catch `OperationCanceledException`.
7. **Omitted arguments** arrive as empty strings, not `null` (`customerName=`, `dateFrom=`). Tool argument parsing must treat empty and whitespace as "not provided".
8. **Latency.** About 25-40 s per turn on this machine's CPU (each turn is one tool round plus the answer). The default `TimeoutSeconds` of 120 leaves room for two tool rounds; the UI must show the thinking indicator the whole time.
