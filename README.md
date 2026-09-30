# swiftbets-payout

[![ci](https://github.com/remonenaidoo/swiftbets-payout/actions/workflows/ci.yml/badge.svg)](https://github.com/remonenaidoo/swiftbets-payout/actions/workflows/ci.yml)

Payouts for SwiftBets: turns settlements into wallet credits and debits by delta (`{coupon}_{bet}_{type}_{version}` idempotency keys), with a named-step, resumable retry ladder on topics (5s, 1m, 15m) instead of sleeping workers, a dead-letter queue, and a wallet blacklist.

## Hosts

- `SwiftBets.Payout.Worker`: payout consumer, ladder consumers and dead-letter replay.
- `SwiftBets.Payout.Migrator`: one-shot DbUp migrator.

## Data and events

- **Owns:** SQL Server `SbPayout` (payment records per coupon and version, step state, outbox, inbox).
- **Events:** Consumes `settlement.coupon-settled` and the ladder topics; produces `payout.payout-completed`, `payout.retry-*`, `payout.dead-letter`.

## Layout

Clean Architecture, enforced by project references and `*.ArchitectureTests`:

```
src/*.Domain          pure domain, no references
src/*.Application     use cases and ports; depends on Domain and contracts only
src/*.Infrastructure  adapters (Dapper + embedded .sql, Kafka, Redis); implements Application ports
src/*.Api | *.Worker  composition root: observability, error envelope, health, metrics
src/*.Migrator        DbUp scripts under Migrations/, run once before the host starts
```

Every host exposes `/health/live`, `/health/ready` (checks its real dependencies), `/metrics` (Prometheus), logs compact JSON with correlation ids, and exports traces over OTLP.

## Build and test

```bash
../swiftbets-platform/scripts/fetch-shared-packages.sh .   # or pack-local.sh for unreleased shared changes
dotnet test SwiftBets.Payout.slnx
```

Integration tests use Testcontainers and need Docker. The whole platform runs from `swiftbets-platform` with `make up`.

## Images

Multi-arch (amd64 + arm64), non-root, chiseled runtime:

- `ghcr.io/remonenaidoo/swiftbets-payout`
- `ghcr.io/remonenaidoo/swiftbets-payout-migrator`

## License

MIT
