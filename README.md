# Chatbox Web — Service Booking Platform

A full-stack booking template for local service businesses (first use case: pet grooming), with a **public site**, **online booking**, **AI chat assistant** (Google Gemini), and an **MVP admin** panel.

## Features

- **Public pages**: home, services, pricing, about, contact, booking wizard.
- **Booking**: pick service, date, and slot; server-side validation, business hours, and **no double booking** for the same start time (serializable transactions + filtered unique index on active rows).
- **Chat**: conversational help — prices, FAQ search, availability, and booking via tools; optional **customer name** (phone required); junk tokens like time-of-day words are not stored as names.
- **Admin**: list/filter bookings by status, update lifecycle (pending → confirmed / cancelled / completed). Customer column shows `null` when no name is stored.

## Tech Stack

| Layer    | Stack |
|----------|--------|
| Frontend | React 19, TypeScript, Vite, React Router |
| Backend  | ASP.NET Core 8 Web API, Swagger |
| Data     | PostgreSQL, Entity Framework Core, Npgsql |
| AI       | Google GenAI SDK (`Gemini`), structured intent extraction + function calling |

## Repository Layout

```text
chatboxweb/
├── backend/
│   ├── BookingTemplate.sln
│   └── src/
│       ├── BookingTemplate.Api/           # HTTP API, DI, startup
│       ├── BookingTemplate.Application/   # Services, DTOs, orchestration
│       ├── BookingTemplate.Domain/        # Entities, enums
│       └── BookingTemplate.Infrastructure/# EF Core, Gemini, tool executors
├── frontend/
│   ├── src/
│   │   ├── components/    # chat, admin, layout, booking UI
│   │   ├── pages/
│   │   └── services/      # API client
│   └── package.json
└── README.md
```

## Prerequisites

- [.NET SDK 8](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20+ (LTS recommended)
- [PostgreSQL](https://www.postgresql.org/) 14+
- A [Google AI Studio](https://aistudio.google.com/) API key for Gemini (optional if you only use the non-chat flows)

## Backend Setup

1. Create a PostgreSQL database and set the connection string.

2. Configure `backend/src/BookingTemplate.Api/appsettings.Development.json` (or User Secrets):

   - `ConnectionStrings:DefaultConnection` — Npgsql connection string.
   - `Gemini:ApiKey` — your Gemini API key (recommended via User Secrets in Development).
   - `Gemini:Model` — defaults to `gemini-2.5-flash`.
   - `BusinessSettings:Timezone` — e.g. `Pacific/Auckland`.

3. From the `backend` folder:

   ```bash
   dotnet restore
   dotnet build BookingTemplate.sln
   dotnet run --project src/BookingTemplate.Api
   ```

   The API profile in `launchSettings.json` listens on **http://localhost:5003** and opens Swagger in Development.

4. On startup in Development, the app calls `Database.MigrateAsync()` and seeds demo data if the database is empty. Ensure migrations exist in your branch; if the database is not ready, startup continues and you can fix the connection later.

## Frontend Setup

1. From the `frontend` folder:

   ```bash
   npm install
   npm run dev
   ```

2. Default dev server: **http://localhost:5173**.

3. Point the UI at your API (see `frontend/.env.development`):

   ```env
   VITE_API_BASE_URL=http://localhost:5003
   ```

## CORS

The API allows the frontend origin `http://localhost:5173`. Add other origins in `Program.cs` if needed.

## Tests

```bash
cd backend
dotnet test BookingTemplate.sln
```

Integration tests may use an isolated SQLite database; the running API process should be stopped if Windows file locks prevent rebuilding.

## Environment Variables (summary)

| Setting | Where | Purpose |
|---------|--------|---------|
| `ConnectionStrings:DefaultConnection` | appsettings / secrets | PostgreSQL |
| `Gemini:ApiKey` | appsettings / User Secrets / env | Chat + intent extraction |
| `VITE_API_BASE_URL` | `frontend/.env*` | API base URL for the SPA |

## License / Reuse

Template-oriented codebase: adapt branding, content under `frontend/src/content`, and domain rules for your business.
