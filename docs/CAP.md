# CAP Theorem Analysis

## Introduction
The CAP theorem states that in the presence of a **network partition**, a distributed system must choose between **Consistency (C)** and **Availability (A)**. Partition tolerance (P) is non-negotiable in distributed systems.

This system simulates two regions (**US-East** and **EU-West**) and explicitly models partition behavior.

---

## Operation-by-Operation CAP Decisions

### 1. Auction Creation
- **Choice**: **CP (Consistency + Partition tolerance)**
- Auctions are **owned** by a region and not replicated.
- Ensures auctions are never created in two places at once.
- Trade-off: lower availability — auction creation fails if region is unreachable.

### 2. Placing a Bid
- **Local Bids**: **CP**
  - Serializable transactions guarantee ordering and correctness.
- **Cross-Region Bids**:
  - With healthy connectivity: executed in owner region (CP).
  - During partition: stored as **pending events** (AP).
- Trade-off: temporary unavailability of immediate feedback in partitions, but eventual consistency guarantees no lost bids.

### 3. Ending an Auction
- **Choice**: **CP**
- Owner region decides the auction end deterministically.
- During partition, local region may end the auction, but reconciliation replays pending cross-region bids.
- Ensures auction integrity.

### 4. Viewing Auction Status
- **Choice**: **Configurable**
  - **Strong**: For final winner determination.
  - **Eventual**: For leaderboards, browsing, monitoring.
- Trade-off: eventual reads may be slightly stale across regions.

---

## Behavior During Partition
- Cross-region bids are enqueued as **pending** (availability).
- Local bids continue with **strong consistency**.
- Auction may end during partition, but reconciliation ensures:
  - On-time pending bids are applied.
  - Late bids (after end time) are ignored.
- Deterministic conflict resolution prevents ambiguity.

---

## Reconciliation After Partition
- Outbox events are replayed.
- Pending bids validated against auction end time.
- Winner re-evaluated based on ordered bid list.
- Auction state transitions to **Reconciled**.

---

## Summary
- **Auction Creation**: CP  
- **Local Bids**: CP  
- **Cross-Region Bids**: CP (healthy) / AP (partition)  
- **Auction Ending**: CP  
- **Auction Viewing**: CP or AP depending on use case  

This balanced approach ensures:
- Strong guarantees within a region.
- Eventual global consistency.
- No lost bids even under partitions.
