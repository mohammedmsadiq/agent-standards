---
name: review-performance
description: Analyse code or a change for performance and resource-usage problems and recommend measured improvements. Use for slow features, high resource usage, or performance-sensitive changes.
---
# Performance review

1. **Clarify the goal:** which operation, what is slow, and the target (latency, throughput, memory, startup).
2. **Find evidence:** existing measurements, profiles, logs or benchmarks. If none exist, propose how to measure
   before recommending changes.
3. **Look for common issues:** inefficient algorithms; repeated I/O or N+1 queries; unbounded data; blocking calls
   on asynchronous or UI threads; excessive allocations or copying on hot paths; missing or unbounded caches;
   chatty network calls; large images or payloads.
4. **Recommend** changes ranked by expected impact versus risk and effort. Prefer simple fixes that keep the code
   readable.
5. **Verify** any change you make with before/after measurements in a representative (Release) configuration,
   and report the numbers.
