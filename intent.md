# Intent: Chat service for administrators

Author: Sanuja Ariyapperuma. Status: agreed (brainstormed 2026-10-10; review Q&A resolved 2026-10-10 and aligned with [spec.md](spec.md))

## Problem

When a new application is introduced, users take time to get used to it. And when the employee with the most experience of the application leaves the company, it is hard for a new employee to pick it up.

## Proposed outcome

Learning an application should not take long. Anyone who knows the business should be able to perform actions through a simple chat. Role-based access control (RBAC) decides what each user can do. Users should not waste time looking for menus or working out which steps to perform in which order.

This is a **sample application**. Its purpose is to show how to use the **tool-calling** capability of an LLM to drive real admin operations safely. It also shows that **an existing web application can gain a chat with minimal changes**. The admin app is built first as an ordinary app, and the chat is then added on top, reusing the same methods the admin pages already call. Anyone who clones the repository should be able to run it with one Docker command, and should understand it from the README and the docs.

### Scenario

FinGuitar is an e-commerce guitar shop. Admins open the chat in the admin panel and:

- ask "what are the pending orders?" or "what orders came in today?" and get a **table** back
- find an order by ID or customer name, or just by describing it ("the Fender order from a guy called John last week")
- say "order 42 is dispatched". The chat asks for confirmation, then updates the order
- say "cancel order 43". Because this cannot be undone, the chat asks **twice** before it acts
- check stock, update prices or stock, look up customers, and ask for simple sales figures

Every change, whether it comes from the chat or the admin pages, appears on an **audit page** that clearly marks the changes made through the chat.

## Affected users and systems

- **Admin users**, in two roles:
  - **Level1**: full access (read and write).
  - **Level2**: read only.
- **Shop visitors**: browse guitars and add them to a cart.

## Decisions

| Topic | Decision |
|---|---|
| Framework | .NET 10 Razor Pages, a single web project plus a test project |
| Database | SQLite |
| Data access | **Dapper** over `Microsoft.Data.Sqlite` with hand-written, parameterised SQL. Schema changes are numbered `.sql` migration scripts applied at startup. |
| Admin auth | ASP.NET Core **cookie authentication**. Users are kept in an `admin_users` table and passwords are hashed with the built-in `PasswordHasher<T>`. There are two roles, `Level1` and `Level2`, and two seeded demo users. An account locks for 5 minutes after 5 failed sign-ins; the lockout message reveals that the account exists, which is accepted for a local demo. (This replaces ASP.NET Core Identity, whose standard storage needs EF Core. It was chosen as the easiest option to build and understand.) |
| Frontend | **Tailwind CSS** with a custom theme. The storefront ("Showroom") has a **white** background, and the admin panel ("Backstage"), including the chat and sign-in, has a **dark** background. Buttons are solid colours; the sunburst gradient is decoration only, with no text on it, so contrast meets WCAG AA. The CSS is built at build time and no CDN is used. |
| Running it | **Docker.** After `git clone`, `docker compose up --build` starts the web app and Ollama, downloads the model, creates the database, and loads the seed data. The only thing needed is Docker. The shop and admin panel start without waiting for the model download; until it finishes, the chat says the model is still downloading. `dotnet run` still works for developers. |
| LLM | `qwen3.5:4b` on local Ollama |
| LLM client | `Microsoft.Extensions.AI` (`IChatClient` with function invocation) and `OllamaSharp` |
| Chat as an add-on | The chat **reuses the admin portal's service methods** instead of having its own. Each tool is a thin adapter around a method an admin page already uses, so the rules, validation, and error messages are the same. New service methods are allowed only if a page can use them too. No chat-specific parameters are added to shared methods. The admin app's audit trail is designed from the start to record where a change came from (admin panel by default, with optional chat fields, and a hook to mark a change as coming from the chat). The only changes to the existing app are a small, listed "seam": registering the chat, a nav link, and showing chat details (badge, quoted request, link) on the audit page. No service, domain rule, or existing table changes. |
| Tool style | A fixed set of typed tools that call the same application services as the admin UI. **No text-to-SQL.** |
| Writes from chat | **Confirm first.** A write tool never changes data. It records a *proposed action*, and the chat shows a Confirm/Cancel card. Data changes only when the user clicks Confirm; typing "yes" does not confirm. **One change per turn**: a second proposal in the same turn is refused. |
| Critical writes from chat | **Confirm twice.** Cancelling an order, changing a price by 20% or more, and setting stock to 0 are critical. After the first Confirm, the server posts a second card that states the consequence, and the change happens only after the second confirm. The server decides what is critical, not the model. |
| Audit | Every change admins make to **shop data** (order status, product created, price or stock) is written to an **append-only audit log** in the same transaction as the change. Seeding, sign-in bookkeeping, and the chat's own records are not audited. Each entry shows who, when, what changed (old → new), and the **source**: Admin panel or Chat. Chat entries quote the request; the user who made the change also gets a link back to the conversation (conversations are private). Both roles can view the audit page. |
| Tables in chat | Rendered by the server **from the structured tool result**. The LLM only writes the short sentence around the table, so it cannot invent or drop rows. |
| Chat history | Saved to SQLite, per user, as separate conversations with a "New chat" button. |
| Storefront | Product list with **photos**, product detail, add to cart (session cart), view cart. **No checkout.** Orders come from seed data. |
| Response mode | Plain request/response with a "thinking…" indicator; no streaming |
| Documentation | Every feature has a plain-English page in `docs/features/`, with Mermaid diagrams for flows. The root README explains the app to a repository visitor and lists the demo logins. It also has a "What it took to add the chat" section that maps each tool to the admin method it reuses and shows which existing files changed. |
| Build order | The admin app and storefront are built first, with no chat code, and tagged `before-chat`. The chat is added afterwards, so `git diff before-chat` shows exactly what adding the chat cost. |

## Scope

### Storefront (`/`, white "Showroom" theme)
- Catalogue of guitars (name, brand, category, price, **product photo**, in stock / out of stock)
- Product detail page with a large photo
- Add to cart and a cart page with quantities and a total, kept in session

### Admin panel (`/Admin`, dark "Backstage" theme, requires `Level1` or `Level2`)
These are the regular operations. Each one is also reachable from the chat, except creating a product and the audit page.
- **Dashboard**: today's orders, pending count, revenue today and this week, and the top sellers this week
- **Orders**: list with filters (order id, status, date, customer, product, total range), order detail with its change history, and status change (Level1 only). Cancelling asks for confirmation first, because it cannot be undone.
- **Products**: list with photos, create, edit price and stock (Level1 only), and change history on the detail page
- **Customers**: list and detail with order history (read only)
- **Chat**: the chat page, with a list of past conversations
- **Audit**: every change to shop data, newest first, with filters (date, source, user, entity). Chat changes carry a **Chat** badge, the quoted request, a "View in chat" link (shown only to the user who made the change), and "Confirmed twice" for critical actions.

### Domain
- `Product`: name, brand, category, price, stock, photo
- `Customer`: name, email, phone, address
- `Order`: customer, order date, status, lines, total
- `OrderLine`: product, quantity, unit price
- `AuditEntry`: when, who, source, entity, action, field changes, and the chat link
- Order status rules, which the domain enforces no matter where the change comes from:
  - `Pending → Dispatched`
  - `Dispatched → Delivered`
  - `Pending → Cancelled`
  - every other change is rejected with a clear message

### Chat tools

| Tool | Purpose | Roles |
|---|---|---|
| `search_orders` | Optional filters: order id, customer name (partial), product name (partial), status, date from/to, min/max total. Returns at most 20 results. | L1, L2 |
| `get_order_details` | One order with its lines and customer | L1, L2 |
| `propose_order_status_change` | Order id and new status. Creates a pending action. | L1 |
| `search_products` | Name, brand, or category (partial); low-stock filter | L1, L2 |
| `propose_product_update` | Product id, new price and/or new stock. Creates a pending action. | L1 |
| `search_customers` | Name, email, or phone (partial) | L1, L2 |
| `get_customer_details` | Customer details plus order history | L1, L2 |
| `get_sales_summary` | Order count and revenue for a date range | L1, L2 |
| `get_top_products` | Best sellers by quantity for a date range | L1, L2 |

The tool count is kept small and the descriptions short, because a 4B model loses accuracy as the tool list grows.

Every tool, and the confirm step, calls a method the admin pages already use: the search tools call the same search methods as the list pages, the details tools call the same methods as the detail pages, the sales tools call the same methods as the dashboard, and Confirm calls the same status-change and product-update methods as the admin buttons and forms.

## Key behaviours

1. **RBAC is enforced twice.**
   - Only the tools the user's role allows are offered to the LLM.
   - Each tool, and the confirm endpoint, checks the role again when it runs.
   - A Level2 user asking to dispatch an order gets a polite "you don't have permission".
2. **Proposed actions are server-side records**: id, user, conversation, action type, arguments, a human-readable summary, whether it is critical, created-at, and status (pending, awaiting final confirmation, applying, confirmed, cancelled, expired, or failed).
   - Confirm only works for the user who created the action, and only once.
   - A card expires when the user sends another message **in the same conversation**, when a newer card appears **in the same conversation**, or after 10 minutes. Cards in other conversations are not affected.
   - A turn can create at most one card. The card tells the user to use its buttons, because typing "yes" sends a new message and expires it.
   - Confirm runs through the same application service as the admin UI, so domain rules still apply.
   - The outcome is posted to the chat by the server, for example "Order 42 is now Dispatched."
3. **Critical actions need a second confirmation.** The first Confirm changes nothing. The server then posts a final card ("Cancelling order 42 cannot be undone. [Yes, cancel order 42] [Keep order]"). The final step is refused unless the first one happened.
4. **Every change is audited.** The service that makes a change writes its audit entry in the same transaction, so a change and its entry always exist together. Entries cannot be edited or deleted.
5. **Finding an order by description.** The LLM turns the description into `search_orders` filters.
   - One match: show its details.
   - Several matches: show a table and ask the user to pick one.
   - No matches: say so and suggest loosening the filters.
6. **Dates.** The system prompt includes today's date, so the model can resolve "today", "last week" and "yesterday" into concrete ISO dates for the tools.
7. **Failures are handled.**
   - Ollama not running: show a friendly error in the chat.
   - Model still downloading: say so in the chat; the rest of the app works.
   - The model runs out of tool rounds (5) without an answer: say "I couldn't finish that. Try asking more specifically."
   - A tool error or a domain rule violation: return it to the model as a message, and the model explains it to the user.
   - Unknown ids: return "not found" instead of throwing.
8. **Stored chat data.** Each message is saved with its role, text, structured tool payload (JSON), and timestamp, so a reloaded conversation shows its tables and cards again.
9. **Same path as the admin pages.** A change made through the chat and the same change made on an admin page end in the same state, with the same audit entry apart from its source. Breaking a rule gives the same error message in both.
10. **Model thinking output** is switched off (`think = false`), and any `<think>` text that still appears is stripped before it is shown or saved.

## Seed data
- Loaded automatically the first time the app starts, so the database is never empty. No database file is committed, because the seed dates are relative to the first start. Fixed facts are anchored so they hold on the first day. After that, relative data such as "today's orders" goes stale, and the README explains how to reset the database to replay the guided scenarios.
- About 15 guitars across brands and categories, some of them low on stock, each with a **photo**. The photos are stored in the repo under licences that allow redistribution, and they are credited in `CREDITS.md`.
- About 10 customers
- At least 45 orders with fixed ids, spread over the last 30 days and every status, including several placed "today". Orders 42, 43, and 44 are Pending (for the dispatch, cancel, and "Keep order" scenarios). John's Les Paul order is on the Wednesday of the previous Monday-based week, so "last week" always finds it.
- Demo users, listed in the root README:

  | Role | Email | Password |
  |---|---|---|
  | Level1 | `level1@finguitar.local` | `Demo@Level1` |
  | Level2 | `level2@finguitar.local` | `Demo@Level2` |

## Constraints
- .NET 10 Razor Pages. Keep the design simple but follow good programming practice:
  - thin page models; application services own the logic (and their SQL)
  - dependency injection, async throughout, and validation at the boundaries
  - no speculative abstractions
- Runs fully locally: SQLite and Ollama, with no cloud calls at run time
- Runs with Docker alone; the data, model, and sign-in keys are kept in Docker volumes
- The model name and Ollama URL go in `appsettings.json` and can be overridden with environment variables
- The UI meets WCAG AA contrast, works at phone width, and can be used with a keyboard

## Non-goals
- Checkout, payments, customer accounts, shipping integrations
- Text-to-SQL or any free-form database access from the LLM
- Streaming responses, multiple languages
- Production hosting: Docker is for running the app locally only
- Cloud LLMs or switching between providers
- User management, password reset, two-factor sign-in
- Product photo upload, and a light/dark theme toggle
- Editing or deleting audit entries or conversations

## Acceptance scenarios
1. Level1 asks "what are the pending orders?" and gets a table of pending orders that matches the Orders page.
2. Level1 asks "show today's orders" and gets only orders dated today.
3. Level1 asks "find the order John placed for a Les Paul last week" and the correct order is found without being given an id.
4. Level1 says "order 42 is dispatched", sees a confirmation card, confirms, and the order shows as Dispatched in the admin UI.
5. Level1 tries to mark a Delivered order as Pending and gets an explanation that the change is not allowed. Nothing changes.
6. Level2 asks the same read questions and gets the same answers. Level2 asks to dispatch an order and is refused. Calling the confirm endpoint directly is also refused.
7. "Which guitars are low on stock?" returns a product table. Level1 "set the stock of product 7 to 12" goes through confirmation.
8. "What was revenue this week?" matches the dashboard figure.
9. Reloading the chat page shows the previous conversation, including its tables.
10. Stopping Ollama gives a clear error in the chat and the app does not crash.
11. A repository visitor understands the app from the README and can run it by following "Getting started".
12. On a machine with only Docker, a fresh clone and `docker compose up --build` give a working app with seed data and photos, and both demo logins work.
13. Data, chat history, and the sign-in survive a container restart. `docker compose down -v` resets everything to fresh seed data.
14. The storefront is white and the admin panel is dark. Both work on a phone and with a keyboard.
15. Five wrong passwords lock the account for 5 minutes.
16. The audit page shows a chat change with the Chat badge, the quoted request, and a "View in chat" link (for the user who made it only), and an admin-panel change with the Admin panel badge.
17. "Cancel order 43" needs two confirmations, and its audit entry shows "Confirmed twice". For order 44, choosing "Keep order" changes nothing.
18. A price change of 20% or more asks twice; a small price change asks once.
19. `git diff before-chat` shows that adding the chat changed only the small listed set of existing files, and that every tool reuses a method the admin pages also use.

## Testing
- Unit tests:
  - the order status rules
  - search filters (Dapper against in-memory SQLite built from the real migration scripts)
  - sign-in and lockout
  - the RBAC checks in tools and on confirm
  - the proposed-action lifecycle (single use, owner only, expiry, the critical two-step flow)
  - the critical-action rules
  - audit entries for every write path, and the audit log refusing edits and deletes
  - the same change made from the admin page and from the chat gives the same result and the same error messages
  - seed data and photo credits
- Tests can be run with `dotnet test` or with Docker alone (`docker build --target test .`)
- Manual run-through of all acceptance scenarios in Docker on a freshly seeded database against a live `qwen3.5:4b`, with the results of every scenario recorded in the README

## Risks and open points
- **Small-model reliability.** `qwen3.5:4b` may pick the wrong tool or fill in arguments badly. To reduce this:
  - keep tool descriptions precise
  - validate tool arguments on the server
  - rely on the confirm step for writes, and the second confirm for critical writes
  - spike tool calling through Ollama first, before building the full app
- **Thinking output.** Switched off, with stripping as a fallback. The spike confirms how to send `think = false` through OllamaSharp.
- **Model tag and speed.** The spike confirms the exact Ollama tag for `qwen3.5:4b`. The first Docker start downloads about 3 GB, and answers are slow on CPU-only machines (an optional GPU compose file helps).
- **Photo licensing.** Use only photos whose licence allows redistribution, and credit each one.
- **Chat logic drifting from the admin app.** Duplicated rules or chat-only SQL would weaken the "minimal changes" story. Prevented by the reuse rule, the "same path" tests, and the `before-chat` diff in the README.
