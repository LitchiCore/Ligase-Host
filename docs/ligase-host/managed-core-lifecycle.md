# Managed core lifecycle

`ApolloInstanceManager` is the sole owner of the managed Sunshine process.
Start and stop operations are serialized, generation-bound, and bounded.

## Stop bounds

The production stop budget is split into independently reported stages:

| Stage | Limit | Purpose |
| --- | ---: | --- |
| `gate` | 1000 ms | acquire the lifecycle serialization gate |
| `gracefulWait` | 250 ms | accept a process that is already naturally exiting |
| `kill` | 750 ms | issue tree termination without blocking the caller forever |
| `postKillWait` | 1500 ms | confirm the process has actually exited |

The maximum internal path is 3500 ms. The Desktop's existing five-second exit
fallback remains a separate last-resort UI guarantee.

The caller cancellation token only cancels that caller's observation and
returns `callerCancelled`. It never cancels the internal cleanup task. This
prevents an abandoned or untracked managed process when a window or caller
stops waiting.

## Machine outcomes

`ApolloStopOutcome` contains only:

- `code`: `stopped`, `alreadyStopped`, `callerCancelled`, `gateTimeout`,
  `killFailed`, `killTimeout`, or `postKillTimeout`;
- `stage`: `observe`, `gate`, `gracefulWait`, `kill`, `postKillWait`, or
  `complete`;
- process ID, generation, and `processStillAlive`;
- `nextAction`: `none`, `retryStop`, or `waitForActiveStop`.

No path, command line, token, certificate, or exception text is included.

## Concurrency and ownership

- all Stop callers for one active generation observe the same internal task;
- a stopped or never-started manager returns `alreadyStopped`;
- Start waits for an active Stop to finish and cannot create a second process;
- a Stop queued behind Start captures the process only after acquiring the
  lifecycle gate, so the new generation is included in that Stop;
- each Exited callback captures its process and generation; an old callback
  cannot mutate a replacement generation;
- an exit confirmed during Stop releases the process handle immediately;
- a timeout retains the live process as managed state for retry; if it exits
  later, the generation-bound callback releases its handle and updates the
  diagnostic state.

No caller waits forever. A platform operation that completes after its stage
timeout remains fault-observed, while the process stays tracked for retry or a
later generation-bound exit callback. A timed-out process is never reported as
stopped.
