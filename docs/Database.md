# Database Design

## Overview
The system uses **EF Core with SQLite (in-memory or file)** for simulation.  
Schema design supports inheritance, ordering guarantees, audit trail, and recovery metadata.

---

## Schema

### Vehicles
- `Id (GUID, PK)`
- `Make`, `Model`, `Year`
- `VehicleType (Discriminator: Sedan, SUV, Hatchback, Truck)`
- Region-specific: vehicles are not replicated.

### Auctions
- `Id (GUID, PK)`
- `VehicleId (FK → Vehicles)`
- `Region`
- `StartTime`, `EndTime`
- `State` (Created, Running, Ended, Reconciled)
- `HighestBidId (FK → Bids)`
- `HighestAmount`
- `Version (long, shadow property, concurrency token)`

### Bids
- `Id (GUID, PK)`
- `AuctionId (FK → Auctions)`
- `UserId`
- `Amount (decimal)`
- `Sequence (long)`
- `OriginRegion`
- `DeduplicationKey`
- `Timestamp`
- Indexes:
  - `(AuctionId, Sequence)` unique
  - `(AuctionId, DeduplicationKey)` unique
  - `(AuctionId, Amount)`

### OutboxEvents
- `Id (GUID, PK)`
- `AggregateType`
- `AggregateId`
- `EventType`
- `PayloadJson`
- `DestinationRegion`
- `CreatedAt`, `ProcessedAt (nullable)`

### AuditEntries
- `Id (GUID, PK)`
- `EntityType`
- `EntityId`
- `Operation`
- `Region`
- `UserId`
- `PayloadJson`
- `OccurredAt`

### PartitionRecovery
- `Id (GUID, PK)`
- `AuctionId`
- `Region`
- `RecoveryState`
- Unique index `(AuctionId, Region)`

---

## Concurrency Handling
- **Optimistic Concurrency**:
  - Implemented with shadow property `Version` (long).
  - Incremented on each update.
  - Detects write conflicts with `DbUpdateConcurrencyException`.

---

## Transaction Boundaries
- Bid placement executed under `IsolationLevel.Serializable`.
- Ensures correct ordering and no phantom reads.
- Updates to auction and bid insertion are atomic.

---

## Indexing Strategy
- **Auctions**: `(Region, HighestAmount)` for fast leaderboards.
- **Bids**: `(AuctionId, Sequence)` for ordering, `(AuctionId, DeduplicationKey)` for idempotency.
- **AuditEntries**: `(EntityType, EntityId, OccurredAt)` for querying history.

---

## Partition Handling
- **Writes during partition**:
  - Local bids accepted.
  - Cross-region bids written to `OutboxEvents`.
- **Recovery**:
  - Pending outbox events replayed after partition heal.
  - Duplicate bids prevented via `DeduplicationKey` index.
- **Eventual consistency**: queries across regions may lag until reconciliation.

---
