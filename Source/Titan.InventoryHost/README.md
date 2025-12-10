# Titan.InventoryHost

## Overview
**Titan.InventoryHost** is an Orleans Silo executable dedicated to hosting inventory-related grains and logic.

## Role in Global Solution
This service handles the heavy lifting of item management. By isolating inventory logic into its own host, the system can scale to handle the high volume of item creation, movement, and updates independent of the login or trading subsystems.

## Key Responsibilities
- **Primary Inventory Hosting**: While `Titan.IdentityHost` also supports inventory grains (via shared configuration), this host is dedicated to handling the bulk of item management traffic.
- **High Throughput**: Optimized for the frequent state changes associated with player inventories.
- **Cluster Participation**: Joins the distributed Orleans cluster.
