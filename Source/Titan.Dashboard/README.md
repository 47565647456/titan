# Titan.Dashboard

Blazor Server web application for managing the Titan game backend. Provides an administrative interface for game operations, player management, and system configuration.

## Overview

The Dashboard is an Orleans **Client** that connects to the Titan cluster, allowing administrators to interact with game Grains through a web-based interface.

## Features

- **ASP.NET Core Identity** - Separate authentication from game players
- **Role-based Authorization** - SuperAdmin, Admin, Viewer roles
- **Orleans Client Integration** - Direct access to game Grains
- **Dark Theme UI** - Modern, responsive design

## Architecture

```
┌─────────────────────────────────────────────┐
│             Titan.Dashboard                 │
├─────────────────────────────────────────────┤
│  Blazor Server Components                   │
│   └── Login, Home, ItemTypes, Seasons...    │
├─────────────────────────────────────────────┤
│  ASP.NET Core Identity                      │
│   └── AdminUsers, AdminRoles (PostgreSQL)   │
├─────────────────────────────────────────────┤
│  Orleans Client                             │
│   └── IItemTypeRegistryGrain, ISeasonGrain  │
└─────────────────────────────────────────────┘
```

## Configuration

The Dashboard is configured via Aspire orchestration. Required configuration:

| Setting | Source | Description |
|---------|--------|-------------|
| `ConnectionStrings:titan-admin` | Aspire | PostgreSQL connection for Identity (Admin DB) |
| `ConnectionStrings:orleans-clustering` | Aspire | Redis for Orleans clustering |

## Running

Start the Dashboard via Aspire:

```powershell
cd Source\Titan.AppHost
dotnet run
```

Access the dashboard at the URL shown in the Aspire dashboard (typically `https://localhost:xxxxx`).

## Default Credentials (Development)

On first startup, the Dashboard seeds a default SuperAdmin account (configured in `appsettings.json`):

- **Email**: `admin@titan.local`
- **Password**: `Admin123!`

> ⚠️ Change these credentials immediately in production environments.

## Roles

| Role | Description |
|------|-------------|
| **SuperAdmin** | Full access including admin user management |
| **Admin** | Game management (items, seasons, players) |
| **Viewer** | Read-only access to all data |

## Project Structure

```
Titan.Dashboard/
├── Components/
│   ├── Layout/           # MainLayout, NavMenu
│   ├── Pages/
│   │   ├── Account/      # Login, Logout, AccessDenied
│   │   ├── Home.razor    # Dashboard home
│   │   └── ...           # ItemTypes, Seasons, Players
│   ├── App.razor         # Root component
│   ├── Routes.razor      # Routing with auth
│   └── _Imports.razor    # Global usings
├── Data/
│   └── AdminDbContext.cs # Identity EF Core context
├── Program.cs            # Host configuration
└── wwwroot/
    └── css/titan-dashboard.css
```

## Dependencies

- **Titan.Abstractions** - Grain interfaces and models
- **Titan.ServiceDefaults** - Aspire defaults, logging, OpenTelemetry
- **Microsoft.Orleans.Client** - Orleans cluster client
- **ASP.NET Core Identity** - Authentication/authorization
- **Npgsql.EntityFrameworkCore.PostgreSQL** - EF Core provider for PostgreSQL
