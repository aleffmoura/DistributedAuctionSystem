# Distributed Car Auction Platform

## Overview
This project is a **technical challenge implementation** of a distributed auction system spanning **US-East** and **EU-West** regions.  
It demonstrates partition handling, CAP theorem trade-offs, consistency guarantees, and reconciliation mechanisms.

---

## Documentation
Additional design and decision documents are available in the [`docs/`](./docs) folder:

- **Architecture.md** – system architecture and components
- **CAP.md** – CAP theorem trade-offs and consistency decisions
- **ConflictResolution.md** – deterministic conflict handling strategies
- **Database.md** – schema design, indexing, and transaction boundaries
---

## 🔎 Test (end-to-end)
The complete challenge scenario (partition, EU↔US bids, completion during partition, reconciliation, no loss) is covered by this test:
Test name: (fully qualified)
DistributedAuction.Tests.EndToEndPartitionFlowTests.Auction_partition_end_to_end_should_select_correct_winner_and_lose_no_bids

- how to run only this one: 
dotnet test --filter "FullyQualifiedName~DistributedAuction.Tests.EndToEndPartitionFlowTests.Auction_partition_end_to_end_should_select_correct_winner_and_lose_no_bids"

- run all class end-to-end
dotnet test --filter "FullyQualifiedName~DistributedAuction.Tests.EndToEndPartitionFlowTests"

- logs verbosity
dotnet test -l "console;verbosity=detailed"

---

## Features
- Vehicle management (CRUD, region-specific).
- English auction (ascending bids).
- Cross-region bid support.
- Partition detection and healing simulation.
- Reconciliation with no lost bids.
- Optimistic concurrency with EF Core.
- Audit trail and outbox pattern.

---

## Architecture
- **Services**:
  - `AuctionService` (auction lifecycle & bids)
  - `BidOrderingService` (sequence numbers)
  - `RegionCoordinator` (partition simulation)
  - `ConflictResolver` (deterministic resolution)
- **Persistence**:
  - EF Core with SQLite (in-memory or file).
  - Optimistic concurrency using `Version` token.
- **Events**:
  - Outbox for cross-region bids.
  - Replay on partition healing.

---

## Setup & Running
### Requirements
- .NET 8 SDK
- SQLite (in-memory used by default)

### Running
```bash
dotnet build
dotnet test
