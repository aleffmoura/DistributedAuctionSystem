# Distributed Car Auction Platform - Architecture

## Overview
This project simulates a distributed car auction platform operating across two geographic regions: **US-East** and **EU-West**.  
The system is not intended to be production-ready but demonstrates understanding of distributed systems trade-offs, CAP theorem implications, and partition handling strategies.

The architecture supports:
- Vehicle management (CRUD, region-specific, no replication).
- Auctions in English ascending bid format.
- Cross-region bidding with consistency trade-offs.
- Partition detection and reconciliation.
- Audit logging and outbox pattern for recovery.

---

## High-Level Architecture

+-------------------------+ +-------------------------+
| US-East Region       	   | | EU-West Region |
| |				       	   | |
| [Auction Service]    	   | | [Auction Service] |
| + AuctionRepo      	   | | + AuctionRepo |
| + BidRepo         	   | | + BidRepo     |
| |						   | |
| [BidOrderingService]	   | | [BidOrderingService] |
| [RegionCoordinator] | <----> | [RegionCoordinator] |
| [ConflictResolver]	   | | [ConflictResolver] |
|						   | |							 |
| [AuctionDbContext]	   | | [AuctionDbContext] |
| - Vehicles			   | | - Vehicles |
| - Auctions			   | | - Auctions |
| - Bids				   | | - Bids |
| - OutboxEvents		   | | - OutboxEvents |
| - AuditEntries		   | | - AuditEntries |
+-------------------------+ +-------------------------+

- **AuctionService**: Orchestrates auction lifecycle and bid handling.
- **BidOrderingService**: Ensures ordering guarantees and sequence numbers.
- **RegionCoordinator**: Simulates network reachability and partitions.
- **ConflictResolver**: Resolves bid conflicts deterministically.
- **AuctionDbContext**: EF Core in-memory/SQLite database with optimistic concurrency.

---

## Core Components

### Auction Service
- Provides operations to create auctions, place bids, fetch auction status, and reconcile after partitions.
- Guarantees **strong consistency** for local bids.
- Uses **eventual consistency** for cross-region bids.

### Region Coordinator
- Simulates **network partitions**.
- Reports region reachability.
- Raises events on partition detection and healing.
- Default: healthy network unless partition explicitly simulated.

### Bid Ordering Service
- Assigns strictly increasing sequence numbers to bids within an auction.
- Ensures tie-breaking on bids with same amount and timestamp.

### Conflict Resolver
- Deterministically selects the winning bid during reconciliation.
- Rules:
  - Highest amount wins.
  - If amounts are equal, earlier sequence wins.
  - If still equal, earliest timestamp and then GUID.

---

## Data Persistence

### Entities
- **Vehicle**: Base type with inheritance for Sedan, SUV, Hatchback, Truck.
- **Auction**: Contains state, region, start/end time, highest bid reference.
- **Bid**: Holds sequence, amount, user, deduplication key, origin region.
- **OutboxEvent**: Pending cross-region events (used for reconciliation).
- **AuditEntry**: Tracks every significant operation (bid, reconcile, partition).
- **PartitionRecovery**: Metadata to track recovery progress.

### Database
- **SQLite** (in-memory for testing).
- Row-level optimistic concurrency for auctions (`Version` shadow property).
- Indexes:
  - `(AuctionId, Sequence)` for bid ordering.
  - `(AuctionId, DeduplicationKey)` for deduplication.
  - `(Region, HighestAmount)` for efficient leaderboards.

---

## Event & Partition Handling

- **Outbox Pattern**: Cross-region bids during partitions are stored in `OutboxEvents`.
- **Reconciliation**:
  1. On partition heal, pending events are replayed.
  2. Only bids placed before `Auction.EndTime` are considered valid.
  3. Auction moves through states: `Running → Ended → Reconciled`.
- **At-least-once delivery**: Ensures no bid is lost during reconciliation.

---

## State Machine

[Created] --> [Running] --> [Ended] --> [Reconciled]

- **Created**: Auction defined but not yet open.
- **Running**: Bids accepted locally and cross-region.
- **Ended**: Auction expired (no new bids).
- **Reconciled**: Post-partition reconciliation applied, final winner decided.

---

## Consistency Choices

- **Auction Creation**: CP — written to owner region with strong guarantees.
- **Local Bids**: CP — serializable transactions ensure order and correctness.
- **Cross-Region Bids**: AP — accepted as pending during partition, replayed on heal.
- **Auction Ending**: CP locally, but reconciliation ensures eventual global consistency.
- **Auction Viewing**:
  - Strong reads for correctness (e.g., determining winner).
  - Eventual reads for performance (e.g., leaderboards).

---

## Trade-offs & Limitations

- Focus is on **simulation** — no real network or replication.
- Outbox events simulate cross-region communication instead of implementing it.
- Only English auctions supported.
- Event bus simplified; no message broker integration.
- No authentication/authorization layer (out of scope).
- Performance is limited by EF Core + SQLite simulation.

---

## Conclusion

This architecture demonstrates:
- Understanding of CAP theorem trade-offs.
- Handling of cross-region partitions with reconciliation.
- Application of optimistic concurrency with EF Core.
- Use of outbox/event-driven techniques to ensure eventual consistency.

The design fulfills all challenge acceptance criteria while keeping the implementation scope focused and demonstrative.
