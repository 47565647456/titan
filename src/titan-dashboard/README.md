# Titan Admin Dashboard

The administrative nerve center for the Titan distributed game backend. This React-based web application provides real-time monitoring and management of the Titan ecosystem.

## Features

- **Real-Time Metrics**: Live visualization of system performance and rate limiting state via SignalR and Recharts.
- **Player Management**: View player profiles, characters, and inventory across all seasons.
- **Season Control**: Create and manage game seasons, including Void Leagues and standard leagues.
- **Registry Management**: Dynamic management of the item base type registry and modifier definitions.
- **Rate Limit Orchestration**: SuperAdmin interface for configuring global rate limiting policies and endpoint mappings on the fly.
- **Role-Based Access**: Secure login with differentiated permissions for Admins and SuperAdmins.

## Tech Stack

- **Frontend**: React 19 + TypeScript + Vite (Rolldown)
- **Data Fetching**: TanStack Query (React Query) for efficient caching and synchronization.
- **Communication**: 
  - Axios for HTTP REST API calls.
  - @microsoft/signalr for real-time WebSocket updates.
- **Styling**: Vanilla CSS (modern flex/grid) with a premium custom design system.
- **Visualization**: Recharts for performance and rate limit metrics.
- **Icons**: Lucide React.

## Getting Started

### Prerequisites
- Node.js 20+
- Running Titan API Gateway (connected to AppHost)

### Development
```bash
# Install dependencies
npm install

# Start development server
npm run dev
```

### Build
```bash
npm run build
```

## Dashboard Sections

| Section | Role Required | Description |
|---------|---------------|-------------|
| **Home** | Admin | System overview and key performance indicators. |
| **Players** | Admin | Search and manage player accounts and character data. |
| **Seasons** | Admin | Manage game lifecycle and league parameters. |
| **Base Types** | Admin | CRUD operations on the item definition registry. |
| **Admin Users** | SuperAdmin | Manage administrative user accounts and roles. |
| **Rate Limiting** | SuperAdmin | Configure dynamic traffic policies and endpoint rules. |
| **Metrics** | SuperAdmin | Real-time monitoring of rate limit buckets and timeouts. |
