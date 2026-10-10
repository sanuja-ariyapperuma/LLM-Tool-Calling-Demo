# Implementation plan: FinGuitar admin chat

Author: Sanuja Ariyapperuma. Status: draft for review (2026-10-10). Sources: [intent.md](intent.md), [spec.md](spec.md)

This plan turns spec 2.11 into ordered, checkable tasks. It does not restate the spec: each task names the files to create, the spec sections it implements, the tests that prove it, and the docs it writes. A task is done when its tests pass, `docker compose up --build` still works, and its feature page is written (spec "Definition of done").

---

## 0. Plan-level decisions

These close gaps found while planning. Items marked **spec edit** need a matching change to `spec.md` in task 1.1, so the spec stays the record.

| # | Gap | Decision |
|---|---|---|
| P1 | The folder is not a git repository, but the build order needs a `before-chat` tag. | Task 1.1 runs `git init`, commits `intent.md`, `spec.md`, `plan.md`, and commits after every task. |
| P2 | Adding the chat must add two NuGet packages (`Microsoft.Extensions.AI`, `OllamaSharp`) and an `Ollama` section to `appsettings.json`. Spec 2.4.2 and acceptance 19 list only `Program.cs`, `_AdminLayout`, and the audit page as the seam. | **Spec edit:** add `FinGuitar.Web.csproj` (two package references) and `appsettings.json` (`Ollama` section) to the seam table. Do **not** pre-add them before the tag; that would hide the real cost. |
| P3 | The chat migration (`0002_chat.sql`) must be picked up without editing the `.csproj`. | The `.csproj` embeds `Data/Migrations/*.sql` with a wildcard from task 2.1, so a new script is a new file only. |
| P4 | Tests need `FakeTimeProvider` and page-level tests (cancel panel, owner-only "View in chat" link, 404 on another user's conversation, anti-forgery). Spec 2.2 lists only `xunit` and `Microsoft.NET.Test.Sdk`. | **Spec edit:** the test project also uses `xunit.runner.visualstudio`, `Microsoft.Extensions.TimeProvider.Testing`, and `Microsoft.AspNetCore.Mvc.Testing` (test-only). The web project gets no extra packages. |
| P5 | "Top sellers" does not say whether Cancelled orders count. | Exclude Cancelled orders, the same as revenue (D7). One `WHERE status <> 'Cancelled'` fragment shared by both `SalesService` queries. **Spec edit** to FR-P1. |
| P6 | `ProposeAsync` needs the conversation id and the request message id, but tools are called by the model with model arguments only. | `ToolResultLog` (scoped, per turn) also carries the turn's `ConversationId` and `RequestMessageId`, set by `ChatService` before the model call. No extra tool parameters. |
| P7 | How the round limit (D22), `think = false` (D4), and "model not found" (D21) surface through Microsoft.Extensions.AI and OllamaSharp is version-dependent. | The spike (task 1.2) records the exact mechanism for each, and `ChatService` is written to those findings. If the function-invocation client makes a final tool-less call after the limit instead of returning tool calls, detect the limit by counting rounds in `ToolResultLog`. |
| P8 | Dates in SQLite. | Store local `DateTime` as `TEXT` in `yyyy-MM-dd HH:mm:ss` via one Dapper `DateTime` type handler, so the format never depends on driver defaults. `DateOnly` is not used in SQL. |
| P9 | The audit "user" filter needs a list of users. | `AuditService.GetUsersAsync()` returns the distinct `(UserId, UserEmail)` pairs from `audit_entries`. No read of `admin_users` from the audit page. |
| P10 | Spike location. | `spike/` at the repo root, listed in `.gitignore`, deleted after task 1.2. Only its findings are kept (in `docs/architecture.md`). |

Toolchain on this machine (checked 2026-10-10): .NET SDK 10.0.401, Docker 29.7, Node 24 (Docker uses the pinned `node:22-alpine`), git 2.55, Ollama 0.40.

---

## Phase 1: Foundations

### 1.1 Repository and spec edits
- `git init`; `.gitignore` (`bin/`, `obj/`, `node_modules/`, `*.db*`, `src/FinGuitar.Web/wwwroot/css/app.css`, `spike/`, `.vs/`).
- Apply the **spec edits** P2, P4, P5 to `spec.md`.
- Commit: "Add intent, spec, and plan".

### 1.2 Spike (spec 2.11 step 1) — gate
Throwaway console app in `spike/` against Ollama in Docker (`docker run -d -p 11434:11434 -v ollama:/root/.ollama ollama/ollama:<pinned>` then `ollama pull qwen3.5:4b`).
- Two tools over an in-memory list: `search_orders(status, customerName, dateFrom, dateTo)` and `propose_order_status_change(orderId, newStatus)`, through `ChatClientBuilder(...).UseFunctionInvocation()`.
- Answer and record:
  1. Exact Ollama tag for `qwen3.5:4b`, and the Ollama image version to pin.
  2. Does tool calling pick the right tool for ~10 prompts from the acceptance list ("pending orders", "today's orders", "John's Les Paul last week", "order 42 is dispatched", "cancel order 43")? Record hits/misses.
  3. How to send `think = false` (`ChatOptions.AdditionalProperties["think"]`, `RawRepresentationFactory`, or an OllamaSharp request option). Confirm no `<think>` text appears.
  4. Are dates passed as `yyyy-MM-dd` when the system prompt gives today's date?
  5. What `UseFunctionInvocation` returns when `MaximumIterationsPerRequest = 5` is hit (P7).
  6. Exception type and message when Ollama is down, and when the model is not pulled (D21, FR-C13).
  7. Rough latency on CPU.
- **Gate:** if tool selection is unreliable (most read prompts miss), stop and revisit the model choice with the user before task 2.1.
- Write findings to `docs/architecture.md` ("Spike findings"). Delete `spike/`.

### 1.3 Skeleton, styles, Docker (spec 2.11 step 2)
Files:
- `FinGuitar.sln`, `src/FinGuitar.Web/FinGuitar.Web.csproj` (net10.0; `Dapper`, `Microsoft.Data.Sqlite`; `<EmbeddedResource Include="Data/Migrations/*.sql" />`), `tests/FinGuitar.Tests/FinGuitar.Tests.csproj` (P4 packages, project reference).
- `Program.cs`: Razor Pages, session (`AddDistributedMemoryCache`, `AddSession`), anti-forgery header `RequestVerificationToken` (NFR-4), `TimeProvider.System`, data protection persisted to `{DataDir}/keys` (2.8.4), HTTPS redirection only outside Docker.
- `appsettings.json`: `ConnectionStrings:Default = Data Source=finguitar.db`. (No `Ollama` section yet, P2.)
- Tailwind v4: `package.json` (exact pinned `tailwindcss` and `@tailwindcss/cli`, scripts `css:build`, `css:watch`), `package-lock.json`, `Styles/app.css` with `@import`, `@source` lines, both themes' `@theme` tokens (2.7.2, 2.7.3), status colours, and the component classes from 2.7.1. Self-hosted Fraunces and Inter `woff2` plus OFL licence files in `wwwroot/fonts/`.
- `Pages/Shared/_StoreLayout.cshtml` (white) and `_AdminLayout.cshtml` (dark, sidebar with Dashboard, Orders, Products, Customers, Audit; phone top bar with a menu button; focus rings; reduced motion). `_ViewStart` picks the store layout; `Pages/Admin/_ViewStart.cshtml` and `Pages/Account/_ViewStart.cshtml` pick the admin layout.
- `Dockerfile` (css → build → test → runtime, 2.8.3), `docker-compose.yml` (`ollama` with health check `ollama list`, `ollama-pull`, `web` depending on `ollama: service_healthy` only; volumes `app-data`, `ollama-models`; env from 2.8.4 minus the `Ollama__*` variables, which arrive with the chat; `WEB_PORT`, `TZ`), `docker-compose.gpu.yml`, `.dockerignore`.

  Note: the compose file already runs Ollama before the chat exists, so the `before-chat` diff does not count compose as a seam change. The `Ollama__*` env lines added later are listed with the seam in the README (P2).
- Docs: `README.md` sections 1, 2, 6, 7 (Docker path, demo logins), 10; `docs/architecture.md` (component and Docker diagrams); `docs/ui-styles.md`.

Done when `docker compose up --build` serves an empty styled storefront page and admin layout on `localhost:8080`, and `docker build --target test .` runs (zero tests is fine here).

---

## Phase 2: Data (spec 2.11 step 3)

### 2.1 Domain
`Domain/`: `Product`, `Customer`, `Order` (with `ChangeStatus(to)` throwing `DomainException` with the FR-D1 text), `OrderLine`, `OrderStatus`, `OrderStatusRules.CanChange` and `AllowedNext(from)`, `ProductRules.Validate(price, stock)` (FR-D2 ranges, throws `ValidationException` carrying field messages), `AuditEntry`, `AdminUser`, `DomainException`, `ValidationException`, `ForbiddenException`.

Tests: `OrderStatusRulesTests` (all 16 pairs), `ProductRulesTests` (bounds 0, 0.01, 100000, 100000.01; stock −1, 0, 10000, 10001).

### 2.2 Database access and migrations
- `Data/Db.cs` (singleton; `OpenAsync` sets `foreign_keys = ON`, `busy_timeout = 5000`), `Data/Money.cs`, Dapper setup in one place: `MatchNamesWithUnderscores`, `Guid` and `DateTime` type handlers (P8).
- `Data/DbMigrator.cs` (2.3): `schema_version`, embedded scripts in number order, one transaction each, `journal_mode = WAL` once.
- `Data/Migrations/0001_initial.sql`: `products`, `customers`, `orders`, `order_lines`, `admin_users`, **`audit_entries` with every column in 2.3 including the chat origin columns**, `Source` CHECK (`AdminPanel`, `Chat`), status CHECKs, indexes (2.3), and the two append-only triggers.
- Startup: migrate, then seed if empty, before `app.Run()`.

Tests (shared `TestDb` fixture: `file:test-{guid}?mode=memory&cache=shared`, one open keeper connection, `DbMigrator` builds the schema): migrate empty → latest; second run is a no-op; foreign keys enforced; `UPDATE`/`DELETE` on `audit_entries` raise.

### 2.3 Seed data and photos
- `Data/SeedData.cs`: 15 products (SD-1: ≥3 low stock, 1 at 0, "Les Paul", "Stratocaster"), 10 customers (one John), ≥45 orders with fixed ids over the last 30 days relative to `TimeProvider` now (SD-3: ≥3 today; 42, 43, 44 Pending; ≥1 Delivered; John's only Les Paul order on the Wednesday of the previous Monday-based week), two demo users hashed with `PasswordHasher<AdminUser>`. Runs only when `products` is empty, in one transaction. Not audited.
- Photos: 15 JPEGs (~800 px wide, < 150 KB) from Wikimedia Commons / Unsplash / Pexels with redistributable licences, `placeholder.jpg`, and `wwwroot/images/products/CREDITS.md` (file, source URL, author, licence). **This needs web access and a human check of each licence**; until done, the seed test for photos fails by design.

Tests: `SeedDataTests` — counts; idempotent; SD-3 facts; John's order is in the previous Monday-based week for a first start on each of the 7 weekdays (fake time); every `ImageFile` exists on disk and is listed in `CREDITS.md`.

Docs: `docs/data-access.md` (Dapper use, adding a migration, money/dates/enums, migrate-then-seed diagram, ER diagram); ER diagram added to `docs/architecture.md`.

---

## Phase 3: Admin app (spec 2.11 step 4)

### 3.1 Sign-in and roles
- `Services/Roles.cs`, `Services/CurrentUser.cs` (`RequireLevel1()`), `Services/AuthService.cs` (2.5.1 steps 1–4, dummy hash for unknown email, lockout through `TimeProvider`).
- `Program.cs`: cookie auth (HttpOnly, Lax, 8 h sliding, paths), policies `Admin` and `Level1`, `AuthorizeFolder("/Admin", "Admin")`, `AuthorizePage` for Products Create/Edit with `Level1`. A page filter (`IAsyncPageFilter`) that turns `ForbiddenException` into 403 → `Account/AccessDenied`.
- Pages: `Account/Login` (dark card plus demo-logins box), `Account/Logout` (POST only), `Account/AccessDenied`.

Tests: `AuthServiceTests` (2.9 sign-in row, fake time); page tests via `WebApplicationFactory`: `/Admin` redirects anonymous users; Logout rejects GET.

### 3.2 Audit trail
- `Services/ChangeContext.cs` (scoped; `Source = AdminPanel` default; `MarkAsChat(...)` present from the start, unused until the chat).
- `Services/AuditService.cs`: `RecordAsync(conn, tx, change)` reading `CurrentUser` and `ChangeContext`; `SearchAsync(AuditFilter, page, 50)`; `GetForEntityAsync`; `GetUsersAsync` (P9).

### 3.3 Services
- `OrderService` (`SearchAsync` with fixed `WHERE` fragments and escaped `LIKE`, `GetDetailsAsync`, `GetAllowedNextStatuses`, `ChangeStatusAsync`: `RequireLevel1` → BEGIN → read → `Order.ChangeStatus` → UPDATE → `AuditService.RecordAsync` → COMMIT).
- `ProductService` (`SearchAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync` with the "no change" check; both writes audited with field-level changes).
- `CustomerService` (`SearchAsync(text,…)`, `GetDetailsAsync` with order history).
- `SalesService` (`GetSummaryAsync`, `GetTopProductsAsync`, `GetDashboardAsync` composed from both; P5).
- Filter records `OrderFilter`, `ProductFilter`, `AuditFilter`.

Tests: `OrderSearchTests` and `ProductSearchTests` (2.9 search row, including page size 25 and 20 through the same method, and wildcard escaping); `SalesServiceTests` (Cancelled excluded from revenue and top sellers, Monday boundary, dashboard equals summary); `RbacServiceTests` (Level2 → `ForbiddenException` on every write, no change, no audit entry); `AuditTests` (one entry per write path with source AdminPanel, field changes, failed rule → no entry and no change, filters and paging).

### 3.4 Admin pages (Backstage theme)
Thin page models calling the services; `ValidationException`/`DomainException` → model-state errors.
- `Admin/Index` (dashboard tiles, today's orders, top 5 this week).
- `Admin/Orders/Index` (every `OrderFilter` field, 25 per page), `Admin/Orders/Details` (lines, customer, status buttons from `GetAllowedNextStatuses` hidden for Level2, `?confirm=cancel` panel per D20, History section with link to filtered audit).
- `Admin/Products/Index` (thumbnails, low-stock filter), `Create`, `Edit`, product History section.
- `Admin/Customers/Index`, `Details`.
- `Admin/Audit/Index` (filters in query string, 50 per page, Source as **plain text**, expandable old → new table, empty state).
- Status badge class via a C# switch with full class names (2.7.1).

Tests (`WebApplicationFactory`): admin cancel — GET `?confirm=cancel` changes nothing, POST "Yes, cancel" changes status and writes one entry; Level2 direct POST to status change → 403 and no change; anti-forgery missing → 400.

Docs: `docs/features/02-sign-in-and-roles.md`, `03-admin-panel.md`, `08-audit-trail.md` (template 2.10.3, diagrams per 2.10.4).

---

## Phase 4: Storefront (spec 2.11 step 5)

- `Services/CartService.cs` (session JSON `productId → quantity`; prices read from `ProductService`; quantity clamped 1..stock; out-of-stock refused).
- Pages: `Index` (hero band with text on a solid panel, card grid, lazy images, fixed ratio), `Products/Details`, `Cart` (Add/Update/Remove handlers, table on desktop, stacked on phone, "Checkout is not part of this demo").

Tests: `CartTests` (2.9 cart row).

Docs: `docs/features/01-storefront.md`.

### Checkpoint: `before-chat`
1. `dotnet test` and `docker build --target test .` pass.
2. `docker compose up --build`: storefront, both logins, every admin page, audit entries from admin edits.
3. Quick UI pass at 375 px and keyboard-only.
4. `grep -ri "chat" src/` finds only the audit origin columns, `ChangeContext.MarkAsChat`, and the `Source` value.
5. `git tag before-chat`.

---

## Phase 5: Chat core (spec 2.11 step 6)

Only new files under `Chat/`, a new migration, and the seam. Before each commit, `git diff --stat before-chat -- src/` must list only seam files as modified.

### 5.1 Seam: packages and config
- `FinGuitar.Web.csproj`: add `Microsoft.Extensions.AI`, `OllamaSharp` (pinned versions from the spike). (P2)
- `appsettings.json`: `"Ollama": { "Endpoint", "Model", "TimeoutSeconds": 120 }`. `docker-compose.yml`: add the `Ollama__*` variables to `web`.
- `Data/Migrations/0002_chat.sql` (new file): `chat_conversations`, `chat_messages`, `proposed_actions`, with foreign keys, CHECKs, and indexes from 2.3.

### 5.2 Proposed actions and critical policy
- `Chat/CriticalActionPolicy.cs` (static; the only place the three rules and the 20% threshold live; returns `(isCritical, warning)`).
- `Chat/ProposedActionService.cs`: `ProposeAsync`, `ExpirePendingForConversationAsync`, `ConfirmAsync`, `FinalConfirmAsync`, `CancelAsync`, shared `ApplyAsync` (claim with `UPDATE … WHERE status = @expected`; product stale-card check via `ProductService.GetAsync`; `ChangeContext.MarkAsChat`; call `OrderService.ChangeStatusAsync` or `ProductService.UpdateAsync`; set `Confirmed` or `Failed`; save ActionResult message). The 10-minute check uses `TimeProvider`.
- `Chat/ChatPayloads.cs` (records for every payload kind, including `ProposalCard` and `FinalConfirmCard`).

### 5.3 Tools
- `Chat/ToolResultLog.cs` (scoped: turn `ConversationId`, `RequestMessageId`, recorded payloads, proposal-made flag, round count; P6, P7).
- `Chat/ChatTools.cs`: nine methods per 2.6.2. Each: role check → argument parsing (ISO dates, case-insensitive status with the valid list, ranges) → **one existing service call** (reuse map 2.4.2) → compact result + payload into `ToolResultLog` → catch `DomainException`/`ValidationException`/`ForbiddenException`/not-found as `{ error }`. D18 guard on both `propose_*`.
- `Chat/ChatToolCatalog.cs`: name, short description, roles; builds the `AIFunction` list for the current role with `AIFunctionFactory.Create(method, target, name, description)`.

Tests:
- `CriticalActionPolicyTests` (2.9 critical row: 19.9% vs 20% up and down, stock 0 vs 1, cancel, dispatch, deliver).
- `ProposedActionLifecycleTests` (2.9 lifecycle row and critical lifecycle row, fake time).
- `ChatToolsTests`: role filtering of the catalog; Level2 calling `propose_*` directly → error; unknown ids → `{ error }`; invalid status lists valid values; D18 second proposal refused.
- `SamePathTests` (2.9): order 42 via admin page vs via chat confirm → same row state and same audit entry except source/chat fields; Delivered → Pending gives identical messages; invalid price gives the same message from Edit and `propose_product_update`; chat audit entries carry conversation, request message id/text, proposal id, confirmation count 1 or 2.

Docs: `docs/features/05-chat-tools.md`, `06-confirm-before-write.md`.

---

## Phase 6: Chat turn and UI (spec 2.11 step 7)

### 6.1 Chat service
- `Chat/ThinkStripper.cs`, `Chat/ChatPrompt.cs` (2.6.4 text, today's date and weekday from `TimeProvider`, role line).
- `Chat/ChatService.cs`:
  - `SendAsync(conversationId?, text)`: validate (FR-C14) → transaction 1 (create conversation with 60-char title, expire pending actions, save user message) → build history (last 20, payload one-line summaries, skip `IsError`, ActionResult as assistant) → model call with role tools, temperature 0.1, `think = false` per spike, timeout `Ollama:TimeoutSeconds` → map errors (unreachable/timeout, model not found, round limit, other) → strip think text → payload rule → transaction 2 (save assistant message, link proposal `MessageId`).
  - Conversation queries (list for user, load with ownership check → null for others).
- Seam, `Program.cs`: bind `Ollama` options; `AddChatClient(new OllamaApiClient(...)).UseFunctionInvocation(f => f.MaximumIterationsPerRequest = 5)`; register `ChatService`, `ChatTools`, `ChatToolCatalog`, `ToolResultLog`, `ProposedActionService`.

### 6.2 Chat page
- `Pages/Admin/Chat/Index.cshtml(.cs)`: GET `?c=` (404 for others' ids), handlers `Send`, `Confirm`, `FinalConfirm`, `Cancel` returning partial HTML; all anti-forgery protected.
- `Pages/Shared/Chat/_Message.cshtml` + partials `_OrderTable`, `_OrderDetail`, `_ProductTable`, `_CustomerTable`, `_CustomerDetail`, `_SalesSummary`, `_TopProducts`, `_ProposalCard`, `_FinalConfirmCard` (live status from DB, buttons only in the card's step and < 10 min, hint text, reversed button order and 1 s disabled danger button on the final card, `id="m{id}"`, rows link to admin pages, text HTML-encoded).
- `wwwroot/js/chat.js`: send with header token, disable while waiting, thinking dots, append returned HTML, card button posts, scroll to `#m{id}`.
- Seam, `_AdminLayout`: "Chat" nav item.
- Seam, `Admin/Audit/Index`: Chat badge with icon, quoted request, owner-only "View in chat" link, "Confirmed twice" marker, Admin panel badge.

Tests:
- `ChatTurnTests` with a scripted fake inner `IChatClient` wrapped in the real function-invocation client (2.9 chat-turn row): payload rule, persistence, history shaping, connection failure, model not found, round limit, message limits, think stripping.
- `ConversationTests` (`WebApplicationFactory`): own conversations only; another user's id → 404 on GET, Send, Confirm, FinalConfirm, Cancel; title truncation.
- Audit page: "View in chat" rendered only for the entry's own user; Chat filter.

Docs: `docs/features/04-chat-assistant.md`, `07-chat-history.md`; chat-turn sequence in `docs/architecture.md`.

---

## Phase 7: Acceptance and release (spec 2.11 step 8)

1. `docker compose down -v && docker compose up --build`; wait for the model pull.
2. Run acceptance scenarios 1–19 in order (13 last) against live `qwen3.5:4b`, Level1 then Level2. Record pass/fail and notes. Take screenshots into `docs/images/` (storefront, admin panel, chat table, confirmation card, final card, audit page).
3. UI check: 375 px and desktop, keyboard only, contrast of both themes.
4. Complete `README.md` sections 3, 4, 5, 5a (from `git diff --stat before-chat -- src/`, seam vs new files, tool → method table), 8, 9, 11, 12, 13, 14.
5. Docker smoke test from a fresh clone in a new folder.
6. Final review: every feature page linked from the README; README and `architecture.md` match the code.

If a scenario fails because of model behaviour (wrong tool, bad arguments), first tighten the tool description or prompt and add a scripted `ChatTurnTests` case for the shape; record any remaining weakness in README section 13 rather than adding chat-only logic.

---

## Traceability

| Spec area | Tasks |
|---|---|
| FR-S1–S4 | 4 |
| FR-A1–A5, 2.5 | 2.3, 3.1, 3.3 |
| FR-P1–P4 | 3.3, 3.4 |
| FR-AU1–AU7 | 2.2, 3.2, 3.3, 3.4, 6.2 |
| FR-D1–D3 | 2.1, 3.3 |
| FR-C1–C19, D2–D6, D16, D18, D21, D22 | 1.2, 5.2, 5.3, 6.1, 6.2 |
| NFR-1–11 | 1.3, 2.2, 3.1, 5.x (NFR-11 by the diff check), 7 |
| DOC-1–7 | every phase; 7 |
| SD-1–5 | 2.3 |
| Acceptance 1–19 | 7 (automated proof per the spec's acceptance mapping) |

## Risks specific to execution

- **Spike outcome** can change the model (task 1.2 gate). Nothing model-specific is built before it.
- **Photo sourcing** needs web access and licence checks; it is the one task that cannot be completed offline. Start it early in parallel with 2.1–2.2.
- **Seam honesty**: if any chat task wants to touch a service, domain, or existing migration file, stop. Either the change belongs in the admin app in a form a page can use (make it, list it in the README as a seam change, and update spec 2.4.2), or the chat handles it inside `Chat/`.
