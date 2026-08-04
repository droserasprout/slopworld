
## Highest-impact findings

| Severity | Finding | Location | Effect / fix |
|---|---|---|---|
| **Critical** | The scroll “throttle” is actually a trailing-edge debounce. Every wheel event resets `_scrollAt = now + 50ms`; the promised immediate first request never occurs. A continuous touchpad gesture may produce no history frames until the gesture stops. | `TerminalWindow`, lines 27–40, 357–362, 995–1005 | Send immediately when no throttle window is active, then send the latest accumulated offset every 50ms. Do not move the deadline on every event. |
| **Critical** | A previous scroll response can clamp a newer request backward. `WindowUpdate` clears `_scrollDirty` immediately when it **sends** the request. During the same or next draw, `settled` becomes true even though the response has not arrived, so `_scrollOff` can be clamped using the old `ScrollScreen`. | `TerminalWindow`, lines 223–241, 357–362 | Track `requestedOff`, `requestId`, and `acknowledgedRequestId`/`inFlight`. Clamp only from the response corresponding to the latest request. |
| **Critical** | Forwarded application-wheel events fork one `tmux` process per wheel notch. One Unity event can generate five WebSocket messages, five daemon operations, five hexadecimal argument lists, and five `tmux send-keys` processes. | `TerminalWindow`, lines 966–980; `session`, lines 1704–1717; `tmux`, lines 366–376 | Batch `count` into one message, generate all reports into one byte buffer, then perform one write. Ideally use a persistent control-client input channel instead of spawning `tmux` for every input event. |
| **High** | WebSocket input is processed serially, and each mouse operation awaits the `tmux` child. Rapid wheel traffic therefore delays later wheel, key, resize, paste, and local-scroll messages. | API/WebSocket file, lines 754–814 | Dispatch input to a bounded per-session queue. Coalesce wheel and resize events, preserve ordering for keys/paste, and keep the socket receive loop nonblocking. |
| **High** | Screen-frame coalescing can send frames out of order. If `pending` contains an older frame and a new frame becomes immediately due, the new frame is sent but `pending` is retained. The older frame can then be flushed afterward. | API/WebSocket file, lines 698–748 | Before an immediate send, discard or replace any pending frame for that pane. Better: keep `pending: HashMap<name, ScreenView>` and never send a sequence lower than the last sent sequence. |
| **High** | The coalescer has only one global `pending` slot although a socket can subscribe to multiple panes. Frames for one pane overwrite pending frames for another. | API/WebSocket file, lines 639–644, 698–730 | Keep one pending frame per session, or formally enforce one subscription per socket. |
| **High** | Scroll offset uses `u16`, while tmux history is configured as `u32`. Valid history beyond 65,535 lines cannot be addressed. Once the C# offset exceeds 65,535, JSON deserialization of `ScrollReq` fails and is silently logged/dropped. | API file, lines 595–600; session lines 2197–2216; tmux lines 9–12, 236–244 | Use `u32` end-to-end, and clamp to a configured maximum before conversion to emulator indices. |

## Backbuffer correctness bugs

### 1. Returning `Off == 0` does not return the UI to live mode

The daemon deliberately returns the live frame when no scrollback is achieved:

```rust
if achieved == 0 {
    return self.screen(name).await;
}
```

But the client only adopts an acknowledged offset when `sb.Off > 0`:

```csharp
if (settled && sb != null && sb.Off > 0)
    _scrollOff = Mathf.Min(_scrollOff, sb.Off);
```

Consequently, if the emulator has no history—or history disappears after a reattach—the UI may draw the live frame while retaining `_scrollOff > 0` and showing the scrollback indicator.

**Fix:**

```csharp
if (settled && sb != null)
    _scrollOff = Mathf.Min(_scrollOff, sb.Off);
```

That alone does not solve the stale-response race; it must be combined with response identity or an in-flight marker.

### 2. Local scrollback loses precedence once the live app changes mode

`HandleWheel` examines the current live frame’s `AppMouse` and `AltScreen` before checking whether the user is already in local scrollback. Thus, while `_scrollOff > 0`, a live application mode change can cause subsequent wheel events to be forwarded into the app instead of navigating the historical view.

Location: `TerminalWindow`, lines 966–997.

A safer decision order is:

1. If `_scrollOff > 0`, always manipulate local history.
2. At live bottom:
   - forward if `AppMouse`;
   - translate to arrows if `AltScreen`;
   - otherwise enter local history.

This prevents an unseen live-screen change from hijacking a gesture over an already displayed historical page.

### 3. No request identity

A scroll reply contains only achieved `off`, not the offset requested or a generation number. That prevents the client from distinguishing:

- the response to the current gesture;
- an older response already in transit;
- a freshly broadcast live frame;
- a response from before a session switch/reconnect.

Add something like:

```rust
struct ScrollReq {
    name: String,
    request_id: u64,
    off: u32,
}
```

and echo `request_id` in the private response. The client should accept only the latest generation for the current session.

### 4. Historical frames probably should not display the live cursor

`scroll_capture` builds a normal `ScreenView`, including cursor position and cursor metadata, and `TerminalWindow.DrawCursor` does not suppress the cursor when `buf.Off > 0`.

Location: session lines 2207–2216; `TerminalWindow`, lines 753–786.

Unless `scroll_snapshot` deliberately relocates/hides it, this paints a live cursor over historical content. Suppress it for scrolled frames:

```csharp
if (buf.Off > 0) return;
```

## Performance bottlenecks

### Full history capture and reconstruction on every reader attachment

On attach/restart/rename, the code:

1. starts `tmux capture-pane` for up to the complete `history_limit`;
2. captures all output into one `String`;
3. splits it into separately allocated `String`s;
4. joins it back into another large seed `String`;
5. feeds the complete seed through the emulator.

Locations:

- tmux lines 236–278;
- session lines 1839–1891.

For a large history limit, this creates several simultaneous copies and many allocations. It can also substantially delay reconnection.

Possible improvements:

- Cap attach seeding independently from tmux’s retention limit.
- Parse capture output incrementally.
- Feed rows directly rather than `split → Vec<String> → join`.
- Reserve the seed capacity when a monolithic seed remains necessary.
- Avoid loading primary history at all when the configured client never exposes that much.

### Scroll snapshot holds a synchronous mutex during frame construction

```rust
let mut e = emu.lock().ok()?;
e.scroll_snapshot(off)
```

Location: session lines 2207–2210.

If `scroll_snapshot` walks/copies the history and renders every row, it holds `std::sync::Mutex<SessionEmu>` throughout. This blocks:

- parsing incoming pane output;
- generating app mouse reports;
- live rendering;
- terminal-query replies;
- OSC-52 extraction.

Because it happens inside an async task, it also blocks the Tokio worker thread itself.

Recommended design:

- Keep scrollback in a ring/deque with efficient indexed viewport extraction.
- Copy only the requested row range while locked.
- Format/SGR-encode the copied rows after releasing the lock.
- Consider `spawn_blocking` only if rendering remains CPU-heavy; it does not resolve lock contention by itself.

### Every scroll response sends a complete frame

Each offset change serializes and transmits all visible rows, then the Unity client reparses all SGR lines and repaints/rebuilds the render texture.

Locations:

- session lines 2197–2216;
- API lines 785–791;
- `TerminalWindow`, lines 378–449 and 1395–1401.

At 20 requests/sec, large panes can generate substantial allocation, JSON, parsing, and GPU churn.

Potential optimizations, in increasing complexity:

- Reduce fixed-rate scroll updates to display cadence.
- Cache snapshots keyed by `(session, live_seq, off, rows, cols)`.
- Send only when the requested offset differs from the last acknowledged one.
- Send row deltas for offsets differing by a few lines.
- Encode screen rows more compactly than repeated JSON strings.

### `send_bytes` has pathological argument construction

```rust
let hexes: Vec<String> =
    bytes.iter().map(|b| format!("{b:02x}")).collect();
args.extend(hexes.iter().map(String::as_str));
```

Location: tmux lines 369–375.

This performs one heap allocation and one process argument per byte. It is costly for mouse traffic and potentially catastrophic for large pastes: argument pointer overhead can reach `ARG_MAX` well before the raw text does.

For wheel reports, batching helps immediately. More generally, use a persistent writable control path or tmux buffer/paste mechanisms for large byte streams.

### Unbounded output bridge

The blocking PTY thread sends every parsed control line through an unbounded channel:

```rust
tokio::sync::mpsc::unbounded_channel::<Vec<u8>>()
```

Location: session lines 1950–1977.

If the async consumer is delayed by emulator locking, tmux subprocess waits, clipboard work, or rendering, memory can grow without limit. Use a bounded channel and backpressure, or read larger chunks and batch control lines.

## Suggested corrected scroll throttle

A leading-edge plus trailing-edge fixed-rate throttle matches the comments and avoids gesture starvation:

```csharp
int _wantedScrollOff;
int _sentScrollOff;
float _nextScrollSend;
bool _scrollPending;
ulong _scrollRequestId;

void QueueScroll(int off)
{
    _wantedScrollOff = Mathf.Max(0, off);
    _scrollPending = true;

    float now = Time.realtimeSinceStartup;
    if (now >= _nextScrollSend)
        SendPendingScroll(now); // first event immediately
}

void SendPendingScroll(float now)
{
    if (!_scrollPending) return;

    _scrollPending = false;
    _sentScrollOff = _wantedScrollOff;
    _nextScrollSend = now + ScrollBeat;

    ulong id = ++_scrollRequestId;
    SessionHub.Instance.RequestScroll(_name, _sentScrollOff, id);
}

public override void WindowUpdate()
{
    base.WindowUpdate();

    float now = Time.realtimeSinceStartup;
    if (_scrollPending && now >= _nextScrollSend)
        SendPendingScroll(now);

    // Existing resize handling...
}
```

On response:

```csharp
void AcceptScroll(ScreenBuf buf)
{
    if (buf.RequestId != _scrollRequestId)
        return;

    _scrollOff = Mathf.Min(_wantedScrollOff, buf.Off);
}
```

## Recommended implementation order

1. Fix stale scroll-response clamping with request IDs/in-flight state.
2. Replace the accidental debounce with leading-edge fixed-rate throttling.
3. Batch app-wheel reports into one daemon message and one tmux write.
4. Fix the WebSocket coalescer’s stale-pending ordering bug.
5. Move offsets from `u16` to `u32`.
6. Prevent local scrollback from being hijacked by live app modes.
7. Profile and optimize `scroll_snapshot` lock duration and full-frame allocation.
8. Replace process-per-input and byte-per-argument transport with a persistent input path.