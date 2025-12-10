# Titan.AppHost

## Overview
**Titan.AppHost** is the orchestration entry point for the Titan solution, built using **.NET Aspire**. It is responsible for defining the distributed application model, configuring infrastructure resources, and managing the startup lifecycle of all services.

## Role in Global Solution
This project is the "glue" that holds the local development environment together. Instead of manually running docker containers and multiple IDE instances, running this project launches the entire stack. It automatically handles service discovery, connection string injection, and environment configuration.

## Key Responsibilities
- **Resource Orchestration**: Defines and runs dependencies like Redis (for Orleans clustering/pub-sub) and PostgreSQL (for data persistence).
- **Service Management**: Launches the `Titan.API` and the various Orleans Silo projects (`Titan.IdentityHost`, `Titan.InventoryHost`, etc.).
- **Distributed Simulation**: Configures **2 Replicas** for each Silo by default to simulate a real-world distributed cluster on your local machine, allowing you to catch concurrency/clustering issues early.
- **Configuration Injection**: Automatically passes connection strings and service discovery endpoints to the running services.
- **Observability**: Hosts the Aspire Dashboard, providing a unified view of logs, metrics, and distributed traces across all projects.

## Usage
To run the full Titan backend solution locally:
1. Ensure Docker Desktop is running.
2. Set `Titan.AppHost` as the startup project in Visual Studio or Rider.
3. Run the project.
