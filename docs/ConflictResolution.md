# Conflict Resolution Strategy

## Introduction
In distributed auctions, conflicts arise from concurrent updates, cross-region bidding, and partitions. This system applies **deterministic rules** to resolve conflicts.

---

## Conflict Scenarios & Resolution Rules

### 1. Concurrent Bids with the Same Amount
- **Rule**: Tie-break by:
  1. **Sequence number** (strictly increasing).
  2. **Timestamp** (earlier wins).
  3. **GUID** (as final deterministic tiebreaker).

### 2. Bids During Partition
- **Rule**:
  - If bid timestamp ≤ `Auction.EndTime` → considered valid, applied during reconciliation.
  - If bid timestamp > `Auction.EndTime` → discarded as late.

### 3. Auction End Time During Partition
- **Rule**:
  - Local region ends auction as scheduled.
  - Reconciliation replays pending cross-region bids.
  - Winner recalculated with valid bids only.
  - Auction transitions to `Reconciled`.

### 4. Database-Level Conflicts
- **Rule**:
  - `Auction` uses optimistic concurrency (`Version` shadow property).
  - If two contexts update simultaneously → `DbUpdateConcurrencyException`.
  - Application retries with refreshed state.

---

## Deterministic Resolution
- Every conflict path results in a **single correct winner**.
- No ambiguity between regions.
- Guarantees that:
  - **No bid is lost.**
  - **Auction integrity is preserved.**
  - **All nodes converge to the same state after reconciliation.**

---

## Example
- **Partition scenario**:
  - EU-West user bids 120 (pending).
  - US-East user bids 100 (accepted).
  - Auction ends while partitioned.
  - On reconciliation: EU bid timestamp is valid → winner is EU (120).
- Outcome: deterministic and correct.
