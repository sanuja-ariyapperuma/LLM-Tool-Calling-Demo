# Chat tools

## What it does

Tools are the only way the assistant can reach shop data. Each tool is a small, typed adapter: it checks the user's role, checks the arguments the model gave, calls **one existing application service method**, and returns a compact result. Tools contain no business rules of their own.

## Who can use it

Developers, mainly. For users: Level1 gets all nine tools, Level2 gets the seven read tools. The two `propose_*` tools are not offered to Level2 at all.

## How to use it

Users never call tools directly: the model does, based on what the user typed. Developers can add a tool by adding a method to `ChatTools` and a line to `ChatToolCatalog`.

## How it works

| Tool | Arguments | Calls (the admin page that uses the same method) | Shows | Roles |
|---|---|---|---|---|
| `search_orders` | orderId, customerName, productName, status, dateFrom, dateTo, minTotal, maxTotal (all optional) | `OrderService.SearchAsync` (Orders list) | Order table (up to 20) | L1, L2 |
| `get_order_details` | orderId | `OrderService.GetDetailsAsync` (Order page) | Order card | L1, L2 |
| `propose_order_status_change` | orderId, newStatus | `OrderService.GetDetailsAsync` and the status rule (Order page buttons) | Confirmation card | L1 |
| `search_products` | text, lowStockOnly | `ProductService.SearchAsync` (Products list) | Product table (up to 20) | L1, L2 |
| `propose_product_update` | productId, newPrice and/or newStock | `ProductService.GetAsync` and `ProductRules.Validate` (Edit form) | Confirmation card | L1 |
| `search_customers` | text | `CustomerService.SearchAsync` (Customers list) | Customer table (up to 20) | L1, L2 |
| `get_customer_details` | customerId | `CustomerService.GetDetailsAsync` (Customer page) | Customer card | L1, L2 |
| `get_sales_summary` | dateFrom, dateTo | `SalesService.GetSummaryAsync` (Dashboard revenue) | Revenue and count | L1, L2 |
| `get_top_products` | dateFrom, dateTo, count (1 to 10) | `SalesService.GetTopProductsAsync` (Dashboard top sellers) | Product ranking | L1, L2 |

Every tool follows the same steps:

1. **Role check** (the two write tools need Level1, and return an error otherwise).
2. **Parse the arguments.** Dates must be `yyyy-MM-dd`, statuses are case-insensitive and an unknown one lists the valid values, numbers must be numbers. The small model sometimes sends an empty string for an argument it did not use, so an empty string means "not provided".
3. **One service call.**
4. **Return** a compact result to the model, and **record** the full result so the server can draw the table.
5. **Errors** (unknown ids, rule violations, bad arguments) come back as `{ "error": "Order 999 not found." }`. Tools do not throw for these, so the model can explain the problem.

### The write tools only propose

`propose_order_status_change` and `propose_product_update` never change data. They check the rules at once (a change that is not allowed is refused and no card is created), save a **proposed action**, and the page shows a Confirm and Cancel card. Details are in [06](06-confirm-before-write.md). A turn can make at most one proposal: a second one returns "One change at a time. Ask the user to confirm this one first."

### Why there is no text-to-SQL

The model could in principle be asked to write SQL. FinGuitar does not do this, because the model could then read or change anything, make a mistake that silently returns wrong rows, or be talked into something harmful. With typed tools the model can only fill in a few arguments, every argument is validated, all SQL is written by developers and uses parameters, and role checks and audit entries apply exactly as they do for the admin pages.

## Rules and limits

- Tools only call public methods of the application services that the admin pages also use, and the domain rule classes those services use. They never run SQL against shop tables.
- A tool needing something new from a service must get it as a method a page could use too.
- Lists are capped at 20 rows and report the total.

## When things go wrong

The model receives the error text and tells the user. Examples: "Unknown status 'shipped'. Valid statuses: Pending, Dispatched, Delivered, Cancelled." and "Order 17 is Delivered and cannot be changed to Pending."

## Where in the code

`Chat/ChatTools.cs`, `Chat/ChatToolCatalog.cs`, `Chat/ToolResultLog.cs`.

## How it is tested

`ChatToolsTests` (role filtering, direct calls by Level2, unknown ids, invalid arguments, one change per turn, the payload rule) and `SamePathTests` (the chat and the admin pages end in the same state with the same messages). Acceptance scenarios 5, 6 and 19.
