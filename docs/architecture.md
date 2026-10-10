# Architecture

FinGuitar is one ASP.NET Core Razor Pages project plus one test project. The admin web app was built first, as an ordinary application with no chat code. The chat was then added as a layer that calls the same application services the admin pages call.

## Components

```mermaid
flowchart LR
    Visitor([Shop visitor]) --> Store[Storefront pages]
    Admin([Admin user]) --> AdminPages[Admin pages]
    Admin --> ChatPage[Chat page]
    ChatPage --> ChatService
    ChatService --> LLM[Ollama<br/>qwen3.5:4b]
    ChatService --> Tools[Chat tools]
    Tools --> Services[Application services<br/>Order, Product, Customer, Sales]
    AdminPages --> Services
    Store --> Services
    ChatPage -- Confirm / Cancel --> Actions[Proposed action service]
    Actions --> Services
    Services --> DB[(SQLite)]
    ChatService --> DB
```

The chat never talks to the shop tables directly. Every read and write goes through the same application services as the admin pages. Pages are thin: they read input, call a service, and show the result. Services own the rules, the SQL, the role checks, and the audit trail.

### Layers

| Folder | Job |
|---|---|
| `Domain/` | Plain classes and rules: `Order.ChangeStatus`, `OrderStatusRules`, `ProductRules`, exceptions |
| `Data/` | `Db` (opens connections), `DbMigrator`, `Migrations/*.sql`, `SeedData`, Dapper setup |
| `Services/` | `OrderService`, `ProductService`, `CustomerService`, `SalesService`, `AuditService`, `AuthService`, `CartService`, `CurrentUser`, `ChangeContext` |
| `Pages/` | Razor Pages: storefront, account, admin |
| `Chat/` | Everything the chat adds (see the chat pages) |

## A change and its audit entry

```mermaid
sequenceDiagram
    participant Caller as Admin page or chat confirm
    participant Svc as OrderService / ProductService
    participant Audit as AuditService
    participant DB as SQLite

    Caller->>Svc: ChangeStatusAsync(42, Dispatched)
    Svc->>Svc: RequireLevel1
    Svc->>DB: BEGIN
    Svc->>DB: read order 42 (old status)
    Svc->>Svc: domain rule check
    Svc->>DB: UPDATE orders
    Svc->>Audit: RecordAsync(Order 42, Pending to Dispatched)
    Audit->>DB: INSERT audit_entries (who, source, old and new)
    Svc->>DB: COMMIT
    Note over Svc,DB: If anything fails: ROLLBACK,<br/>no change and no audit entry
```

The role check comes first, then the change and the audit entry commit together or not at all. The source (Admin panel or Chat) comes from `ChangeContext`, so the method signatures do not change for the chat.

## A chat turn

```mermaid
sequenceDiagram
    actor Admin
    participant Page as Chat page (chat.js)
    participant Chat as ChatService
    participant DB as SQLite
    participant LLM as Ollama model
    participant Tools as Chat tools
    participant Svc as Application services

    Admin->>Page: types "what are the pending orders?"
    Page->>Chat: POST Send (text, conversationId)
    Chat->>Chat: validate (1 to 1,000 characters)
    Chat->>DB: one transaction: create conversation if needed,<br/>expire this conversation's open cards, save user message
    Chat->>LLM: system prompt + last 20 messages<br/>+ tools allowed for this role
    loop up to 5 tool rounds
        LLM->>Tools: search_orders(status = Pending)
        Tools->>Svc: OrderService.SearchAsync
        Svc->>DB: query
        Tools-->>LLM: compact JSON result
        Tools->>Tools: record the full result for the page
    end
    LLM-->>Chat: one or two sentences
    Chat->>Chat: strip think text, choose the payload
    Chat->>DB: save assistant message (text + table or card)
    Chat-->>Page: HTML for both messages
    Page-->>Admin: sentence + table drawn by the server
```

The two halves of a turn are separate transactions because the model call sits between them. If the model fails, an error message is saved instead of a reply.

## How the chat is plugged in (the seam)

The chat is a layer on top of an app that was complete without it. It adds new files under `Chat/`, a migration for its own tables, a page, partials and a script. It changes only a handful of existing files:

| Existing file | Change |
|---|---|
| `Program.cs` | Register the model client, the chat services and the Ollama options |
| `FinGuitar.Web.csproj` | Add `Microsoft.Extensions.AI` and `OllamaSharp` |
| `appsettings.json` | Add the `Ollama` section |
| `Pages/Shared/_AdminLayout.cshtml` | Add the "Chat" menu item |
| `Pages/Admin/Audit/Index.cshtml` | Show the Chat badge, quote, owner-only link and "Confirmed twice" |

Everything else the chat needs it gets by calling the same service methods the admin pages call. The only hook is `ChangeContext.MarkAsChat`, which was part of the audit design from the start.

```mermaid
flowchart TB
    subgraph existing [Existing admin web app: built first, services unchanged by chat]
        Pages[Admin pages] --> Svc[Application services<br/>Order, Product, Customer, Sales, Audit]
        Svc --> Domain[Domain rules + validation]
        Svc --> DB[(SQLite)]
    end
    subgraph chat [Chat add-on: new code only]
        ChatPage[Chat page] --> ChatSvc[ChatService + LLM]
        ChatSvc --> Tools[Chat tools<br/>thin adapters]
        ChatPage --> PA[ProposedActionService]
    end
    Tools -- same public methods --> Svc
    PA -- same public methods --> Svc
    PA -. MarkAsChat .-> Ctx[ChangeContext<br/>the one hook]
    Ctx -.-> Svc
```

## Docker

```mermaid
flowchart LR
    Browser([Browser<br/>localhost:8080]) --> Web
    subgraph compose [docker compose]
        Web[web<br/>ASP.NET Core app] -- http://ollama:11434 --> Ollama[ollama<br/>LLM server]
        Pull[ollama-pull<br/>runs once] -- pulls qwen3.5:4b --> Ollama
    end
    Web --- AppData[(app-data volume<br/>SQLite db + keys)]
    Ollama --- Models[(ollama-models volume<br/>downloaded model)]
```

| Service | Job |
|---|---|
| `ollama` | Serves the model. Pinned image `ollama/ollama:0.40.2`. Health check: `ollama list`. Its port is not published to the host. |
| `ollama-pull` | Runs `ollama pull qwen3.5:4b` once, then exits. |
| `web` | The app on port 8080. It waits only for `ollama` to be healthy, not for the download, so the shop and admin panel work at once. |

```mermaid
sequenceDiagram
    actor Dev as Person who cloned the repo
    participant DC as docker compose
    participant O as ollama
    participant P as ollama-pull
    participant W as web
    participant DB as SQLite (volume)

    Dev->>DC: docker compose up --build
    DC->>W: build image
    DC->>O: start, wait until healthy
    par model download
        DC->>P: start
        P->>O: pull qwen3.5:4b (about 3 GB, first run only)
        P-->>DC: exit 0
    and app start
        DC->>W: start
        W->>DB: apply migrations
        W->>DB: database empty, so seed products, customers, orders, demo users
        W-->>Dev: ready at http://localhost:8080
    end
```

The model and the database live in named volumes, so later starts download and seed nothing. `docker compose down -v` removes both.

## Data model

See the ER diagram in [data-access.md](data-access.md).

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
