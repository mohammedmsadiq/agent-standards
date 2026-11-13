# Performance

- **Measure first.** Do not optimise without a measurement, a target, or a known hot path. Record before/after
  numbers with the change.
- **Use the right algorithm and data structure** before micro-optimising.
- **Avoid unnecessary work:** repeated I/O or queries in loops (N+1), unbounded result sets, redundant
  serialisation, excessive allocations on hot paths.
- **Be asynchronous for I/O** where the platform supports it; never block on asynchronous work.
- **Cache deliberately:** define what is cached, for how long, its size bound, and how it is invalidated.
- **Bound everything:** collections, queues, timeouts, retries, payload sizes and concurrency.
- **Keep the UI responsive:** no blocking work on the UI thread; virtualise long lists; size images appropriately.
- **Startup and memory matter** for client and mobile apps: defer non-critical initialisation.
- Use the `review-performance` skill or the `performance-engineer` agent for performance-sensitive changes.
