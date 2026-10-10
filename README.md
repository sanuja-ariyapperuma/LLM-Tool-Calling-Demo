# FinGuitar: a sample admin chat that uses LLM tool calling to run real shop operations safely

## Why this exists

Admins of a web shop spend a lot of time clicking through filters to answer simple questions ("which orders are pending?") and to make small changes ("order 42 is dispatched"). A chat box would be faster, but letting a language model change real data is risky: it can pick the wrong order, invent numbers, or act without being asked.

FinGuitar is a small guitar shop with an admin panel that shows how to add such a chat **safely**, and how little it takes to add it to an application that already exists. The model can only call a fixed set of typed tools. Every change needs a click on a Confirm button, risky changes need a second click, every change is written to an append-only audit log, and the roles of the signed-in user are enforced in the code that changes the data.

<!-- Sections 3 to 5a (what you can do, how it works, how it stays safe, what it took to add the chat) are completed with the acceptance run. -->

## Tech stack

| Part | Why |
|---|---|
| .NET 10, Razor Pages | One web project, thin pages, built-in sign-in and anti-forgery |
| Dapper + SQLite | Hand-written SQL, no ORM, a single database file that is easy to reset |
| Cookie authentication, `PasswordHasher` | Part of ASP.NET Core, so no extra package; roles `Level1` and `Level2` |
| Tailwind CSS v4 | Two custom themes (white shop, dark admin), built at build time, no CDN |
| Microsoft.Extensions.AI + OllamaSharp | A standard chat-client interface with automatic tool calling |
| `qwen3.5:4b` on Ollama | Small enough to run on a laptop, good at picking the right tool |
| Docker Compose | One command starts the app, Ollama, and the model download |

## Getting started

### Demo logins

| Role | Email | Password | Can do |
|---|---|---|---|
| Level1 | `level1@finguitar.local` | `Demo@Level1` | Everything: read, and change orders and products |
| Level2 | `level2@finguitar.local` | `Demo@Level2` | Read only |

These are demo-only passwords. They are public in this repository.

### Run with Docker

You only need [Docker](https://docs.docker.com/get-docker/).

```
git clone <this repository>
cd <folder>
docker compose up --build
```

Then open:

- the shop: <http://localhost:8080>
- the admin panel: <http://localhost:8080/Admin>

The first run downloads about 3 GB for the language model and may take several minutes. The shop and admin panel work straight away. The chat says "still downloading" until the download finishes. The database is created and filled with sample data on the first start.

### What is in the sample data

15 guitars (a few low on stock, one out of stock), 10 customers, and 48 orders over the last 30 days. Orders 42, 43 and 44 are Pending. Dates are relative to the first start, so the "today" orders are only "today" on the first day. To replay the guided tour on a later day, reset the database (see the commands below).

### Useful commands

| Goal | Command |
|---|---|
| Start everything | `docker compose up --build` |
| Stop | `docker compose down` |
| Reset the database and start fresh | `docker compose down -v`, then `up` again (this also deletes the downloaded model) |
| Reset only the database (fresh dates) | `docker compose down`, then `docker volume rm <project>_app-data`, then `up` |
| Run the tests | `docker build --target test .` |
| View the app logs | `docker compose logs -f web` |

### Settings

- **Time zone:** set `TZ` so "today" matches yours: `TZ=Europe/London docker compose up`.
- **Port:** set `WEB_PORT` if 8080 is taken: `WEB_PORT=9090 docker compose up`.
- **GPU:** `docker compose -f docker-compose.yml -f docker-compose.gpu.yml up` gives Ollama an NVIDIA GPU.
- **Model:** the model name is set in `docker-compose.yml` (`ollama-pull`) and, once the chat exists, in the `Ollama__Model` variable.

### Run without Docker (for developers)

You need the .NET 10 SDK and Node 22 or newer (and, for the chat, a local Ollama with `ollama pull qwen3.5:4b`).

```
cd src/FinGuitar.Web
npm ci
npm run css:build
cd ../..
dotnet run --project src/FinGuitar.Web
```

### Troubleshooting

- **The page has no styling:** the CSS was not built. Run `npm ci && npm run css:build` in `src/FinGuitar.Web`.
- **Port already in use:** set `WEB_PORT`.
- **Slow answers:** on a CPU-only machine each chat answer can take 30 seconds or more. A GPU helps.
- **Something else:** `docker compose logs -f web`.

## Project structure

```
FinGuitar.sln
src/FinGuitar.Web/
  Domain/      Plain classes and business rules (order status rules, product rules)
  Data/        Database access, migrations (SQL scripts), seed data
  Services/    Application services used by every page; sign-in, audit, cart
  Pages/       Razor Pages: storefront, account, admin
  Styles/      Tailwind input and both themes
  wwwroot/     Fonts, images (product photos), built CSS, scripts
tests/FinGuitar.Tests/   xUnit tests on an in-memory SQLite database
docs/                    Architecture, data access, UI styles, one page per feature
Dockerfile, docker-compose.yml, docker-compose.gpu.yml
intent.md, spec.md, plan.md   What was asked for, the design, and the build plan
```

<!-- Sections 3-5a, 8, 9, 11, 12, 13 and 14 are completed with the acceptance run. -->
