# Reusable Service Booking Platform Template

A production-oriented booking platform template for local service businesses, first implemented as a pet grooming website.

This project is designed to be reused across businesses such as pet grooming, beauty clinics, nail salons, massage studios, and barber shops with minimal code changes.

## Project Overview

This repository contains:

- A React + Vite frontend for public pages, booking flow, and MVP admin operations
- An ASP.NET Core Web API backend for booking logic and business rules
- PostgreSQL + Entity Framework Core for persistent storage

The first concrete implementation targets a local pet grooming business, while the architecture is intentionally modular to support future business-template reuse.

## Tech Stack

### Frontend

- React 19 + TypeScript
- Vite
- React Router
- CSS-based responsive UI

### Backend

- ASP.NET Core Web API (.NET 8)
- Entity Framework Core
- Npgsql EF Core provider
- Swagger/OpenAPI

### Database

- PostgreSQL

## Features

### Public Website

- Home, Services, Pricing, About, Contact pages
- Business-oriented, clean responsive design

### Booking Flow

- Select service
- Select booking date
- Load available time slots
- Submit customer and pet details
- Server-side validation before save

### Admin (MVP)

- List bookings
- Filter bookings by status
- Update booking status (pending/confirmed/cancelled/completed)

### Platform Foundations

- FAQ-ready data model and extensible architecture for future chatbot integration
- Clear separation of concerns between API, application logic, domain, and persistence

## Project Structure

```text
chatboxweb/
├─ backend/
│  ├─ BookingTemplate.sln
│  └─ src/
│     ├─ BookingTemplate.Api/              # Web API entrypoint
│     ├─ BookingTemplate.Application/      # DTOs, services, use-case logic
│     ├─ BookingTemplate.Domain/           # Entities, enums, domain models
│     └─ BookingTemplate.Infrastructure/   # EF Core, DbContext, data access
└─ frontend/
   ├─ src/
   │  ├─ components/                       # Reusable UI and layout components
   │  ├─ content/                          # Editable business-facing content
   │  ├─ pages/                            # Public pages + booking + admin
   │  └─ services/                         # API client and request functions
   └─ package.json
```

## Setup Instructions

## 1) Prerequisites

- .NET SDK 8.x
- Node.js 20+ (or newer LTS)
- PostgreSQL 14+

## 2) Backend Setup

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project src/BookingTemplate.Api
```

Backend config example is in:

- `backend/src/BookingTemplate.Api/appsettings.Development.json`

Update connection string as needed:

- `ConnectionStrings:DefaultConnection`

## 3) Frontend Setup

```bash
cd frontend
npm install
npm run dev
```

Frontend runs on `http://localhost:5173` by default.

## 4) Environment Notes

- Backend CORS currently allows `http://localhost:5173`
- API base URL in frontend defaults to `http://localhost:5000` if `VITE_API_BASE_URL` is not set

## Future Improvements

- Add authentication/authorization for admin routes
- Add FAQ public API + admin CRUD UI
- Add business profile configuration (name, colors, logo, hero content) from database
- Add multi-business support (`business_id` / slug-based routing)
- Add chatbot orchestration layer:
  - FAQ retrieval
  - price inquiry responses
  - availability checks
  - booking assistant flows
- Add test coverage (unit + integration + basic end-to-end)
- Add Docker Compose for full local environment

## Why This Project Is Useful for Real Businesses

- Solves a real operational pain point: manual appointment coordination
- Improves customer experience through online self-service booking
- Provides admin visibility for daily booking management
- Reduces no-shows/miscommunication by enforcing structured booking details
- Reusable template reduces implementation time for new local-service clients

## Resume Bullet Points

- Built a reusable full-stack booking platform template (React + ASP.NET Core + PostgreSQL) with modular architecture for local service businesses and first deployment as a pet grooming website.
- Implemented end-to-end booking workflow with real-time slot availability, server-side conflict validation, and status lifecycle management for operational reliability.
- Delivered an MVP admin console for booking operations (list/filter/status updates) and structured the codebase for future FAQ retrieval and chatbot-assisted booking expansion.

## Interview-Style Project Summary

I built a reusable booking platform template for local service businesses, using React on the frontend and ASP.NET Core Web API with PostgreSQL on the backend. The first implementation was a pet grooming site, but I designed the system to be business-agnostic through clean separation of domain, application, and infrastructure layers, configurable content, and modular booking services. The platform includes public-facing pages, a validated booking flow with availability checks, and an MVP admin panel, while also preparing the architecture for future chatbot features such as FAQ answering and booking assistance.
