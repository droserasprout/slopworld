# Next: fixes from review.md

Implementation order — each step builds on the one before it.

---

## 1. Request identity for scroll responses

### What

The C# side has no way to tell whether a `ScreenBuf` is the answer to the current
scroll request, a stale one that was in flight, a live broadcast, or data from before
a session switch. A reply from before a reconnect can clamp the offset backward.

### Changes

**Daemon — `ScrollReq` / `Scroll` response** (`api.rs`, `session.rs`)

- Add `request_id: u64` to `ScrollReq` in `api.rs`.
- Add `request_id: u64` to `ScreenView` (`session.rs`, default 0 — live frames and
  session-list screens won't carry one).
- `scroll_capture` in `session.rs` echoes the received `request_id` into the view.
- The WS loop (`api.rs`) reads it from the deserialised `ScrollReq` and passes it
  through `m.scroll_capture(name, off, request_id)`.

**Mod — `SessionHub` / `ScreenBuf`** (`SessionHub.cs`)

- Add `ulong ScrollRequestId` to `ScreenBuf`.
- `RequestScroll(name, off)` gains a `ulong id` parameter.
- On response, store `request_id` from the JSON into the `ScreenBuf`.

**Mod — `TerminalWindow`** (`TerminalWindow.cs`)

- Maintain `ulong _scrollRequestId`, incremented per request.
- Store the sent id and the acknowledged id.
- `AcceptScroll(buf)`: only adopt `buf.Off` when `buf.ScrollRequestId >= _scrollRequestId`
  (or equals the latest sent — either works as long as stale replies are ignored).
- This replaces `settled` as the guard against stale clamping.

### Why first

Every other scroll fix depends on distinguishing current data from old data. Without
this, the throttling fix and the `Off > 0` gate fix both produce wrong behaviour
when replies arrive late.

---

## 2. Send scroll request immediately, then throttle

### What

The current `_scrollAt = now + 50ms` is a trailing-edge debounce: every wheel event
pushes the deadline forward, so a continuous gesture produces no requests until the
fingers come off. The comments say "leading edge" but the code does not.

### Changes

**`TerminalWindow.cs`** — replace the throttle logic in `HandleWheel` and
`WindowUpdate`.

```
int _wantedScrollOff;
int _sentScrollOff;
float _nextScrollSend;
bool _scrollPending;
ulong _scrollRequestId;

void QueueScroll(int off)  // called from HandleWheel
{
    _wantedScrollOff = Mathf.Max(0, off);
    _scrollPending = true;
    if (Time.realtimeSinceStartup >= _nextScrollSend)
        SendPendingScroll();  // first event fires immediately
}

void SendPendingScroll()
{
    if (!_scrollPending) return;
    _scrollPending = false;
    _sentScrollOff = _wantedScrollOff;
    _nextScrollSend = Time.realtimeSinceStartup + ScrollBeat;

    ulong id = ++_scrollRequestId;
    SessionHub.Instance.RequestScroll(_name, _sentScrollOff, id);
}

// In WindowUpdate:
if (_scrollPending && Time.realtimeSinceStartup >= _nextScrollSend)
    SendPendingScroll();
```

On response:

```
if (buf.ScrollRequestId < _scrollRequestId) return;  // stale
_scrollOff = Mathf.Min(_wantedScrollOff, buf.Off);
```

### Why after request IDs

The throttle produces more requests closer together. Without request identity,
one response could still clamp the offset of a later, larger request.

---

## 3. Fix `Off == 0` refusing to clear `_scrollOff`

### What

The daemon returns the live frame when `off == 0` or no scrollback is achieved. The
client only adopts `sb.Off` when `sb.Off > 0`, so the UI can be stuck at `_scrollOff > 0`
drawing a static scrollback frame while the daemon is already giving the live view.

### Changes

**`TerminalWindow.cs`** — simplify the acceptance gate:

```csharp
if (settled && sb != null)
    _scrollOff = Mathf.Min(_scrollOff, sb.Off);
```

Remove the `&& sb.Off > 0` condition. Combined with request identity (step 1),
`sb` is either the correct response or it's stale, and if it's correct and says
`Off == 0`, the offset should go to zero.

### Why after request identity

Without step 1, removing `Off > 0` reintroduces the stale-response race: an
old reply carrying `Off == 0` would snap `_scrollOff` to zero mid-gesture.

---

## 4. Batch app-wheel reports into one message

### What

`HandleWheel` in `TerminalWindow.cs` loops `k` times (1–5 per event) and calls
`SendMouse` for each notch. Each call spawns a `tmux send-keys` process. One Unity
event can generate five WebSocket messages, five hex conversions, five tmux spawns.

### Changes

**`TerminalWindow.cs`** — `HandleWheel` (app-mouse branch):

```csharp
// After computing `step`, `cell`, `up`:
string act = up ? "wheelup" : "wheeldown";
SessionHub.Instance.SendMouse(_name, act, 0, cell.x, cell.y, step);
```

**`SessionHub.cs`** — `SendMouse` gains an `int count` parameter (default 1).

**`api.rs`** — `MouseReq` gains `#[serde(default)] count: u8`.

**`session.rs`** — `send_mouse` observes `count`:

The emulator's `mouse_report` generates the correct SGR sequence repeated
`count` times, or the existing loop moves inside the emulator where the hex
encoding happens once.

### Why after the throttle

Batching creates fewer, larger requests. Without the throttle fix (step 2),
the batch still sits behind a debounce and a gesture produces one batch at
the end of it, which is better than five but still wrong.

---

## 5. Fix WebSocket coalescer stale-pending ordering bug

### What

The coalescer at `api.rs` lines ~698–748 keeps one `pending: Option<Event>`.
When a Screen event arrives and is due immediately, it's sent and `pending`
is not cleared. The next flush then sends the older frame after the newer
one. It is also one global slot but a socket can subscribe to multiple
panes, so frames for pane B overwrite pending frames for pane A.

### Changes

**`api.rs`** — replace `pending: Option<Event>` with:

```rust
pending: HashMap<String, Event>
```

keyed on `screen.name`. On each Screen event:

1. If due immediately → send, **remove any pending entry for that pane**.
2. If not due → insert/replace under that pane's name.

On flush: send the oldest-due entry (by arrival order, not name). Or simply
iterate and send all whose deadline is past.

On a non-Screen event, flush all pending frames before sending the non-Screen
event, so sessions / quit don't get delayed behind a stale frame.

### Why now

Stale-ordering bugs cause flicker and misrendering that look like other
scroll problems. Fixing the coalescer removes a confound from debugging
the later scroll-snapshot optimisations.

---

## 6. Move scroll offsets from `u16` to `u32`

### What

`ScreenView.off` s `u16`, `scroll_capture` takes `u16`, `ScrollReq.off` is
`u16`, and `_scrollOff` in C# is `int`. Tmux's `history_limit` is `u32`. A
history beyond 65535 lines causes JSON deserialization to fail silently.

### Changes

| Location | Field | Change |
|---|---|---|
| `session.rs` — `ScreenView::off` | `u16` → `u32` |
| `session.rs` — `ScreenView::from_frame` param | `u16` → `u32` |
| `session.rs` — `scroll_capture` param & call | `u16` → `u32` |
| `session.rs` — `emu.scroll_snapshot` call arg | `u16` → `u32` |
| `api.rs` — `ScrollReq::off` | `u16` → `u32` |
| `api.rs` — WS handler calls | `u16` → `u32` |
| `emu.rs` — `scroll_snapshot` param | `u16` → `u32`; clamp to `u16::MAX` before passing to alacritty API |
| `TerminalWindow.cs` — `_scrollOff` | stays `int` (C# convention) |
| `SessionHub.cs` — `ScrollReq.off` / `ScreenBuf.Off` | `int` → stays `int` |

The alacritty terminal grid's `display_offset` is `usize`. Clamp at the boundary:
```rust
let alacritty_off = (off as usize).min(self.term.grid().history_size());
```

Then convert the achieved offset back to `u32`:
```rust
let achieved = self.term.grid().display_offset() as u32;
```

### Why after request identity

Changing the wire format while also changing the request/response matching
would make debugging ambiguous: is the deserialisation failure a type mismatch
or a stale-reply bug? Lock in request identity first.

---

## 7. Prevent local scrollback from being hijacked by live app modes

### What

`HandleWheel` checks `AppMouse` and `AltScreen` on the *live* frame before
checking whether `_scrollOff > 0`. If the app changes mode while the user
is scrolled back, subsequent wheel events get forwarded into the live app
instead of continuing to navigate history.

### Changes

**`TerminalWindow.cs`** — reorder the decision in `HandleWheel`:

```csharp
// 1. Already in scrollback: stay there.
if (_scrollOff > 0)
{
    // continue navigating history (existing code)
    ...
    return;
}

// 2. At live bottom: does the app want the wheel?
if (live != null && live.AppMouse) { ... }
if (live != null && live.AltScreen) { ... }

// 3. Enter scrollback.
...
```

### Why now

This is a small, contained logic change that becomes riskier once the
scrollback uses request IDs and different offset types. Gettng it right
before those land means the logic is verified on the current types.

---

## 8. Suppress the live cursor on historical frames

### What

`DrawCursor` in `TerminalWindow.cs` draws the cursor position and shape
from the `ScreenBuf` even when `buf.Off > 0` (a scrollback frame). The
daemon's `scroll_snapshot` already hides the cursor (`hide_cursor: true`),
but the frame still carries `cx`/`cy` from the rendered snapshot. The
cursor blink and shape may still draw.

### Changes

**`TerminalWindow.cs`** — at the top of `DrawCursor`:

```csharp
void DrawCursor(Rect body, ScreenBuf buf, float cw, float ch)
{
    if (buf.Off > 0) return;   // no cursor in scrollback
    // ...existing code...
}
```

### Why now

Trivial fix, no dependencies. Gets it out of the way.

---

## 9. Profile and optimise `scroll_snapshot` lock duration

### What

`scroll_snapshot` in `emu.rs` holds `Mutex<SessionEmu>` while it scrolls the
display, renders every row, serialises to a `Frame`, collects SGR lines, and
scrolls back. This blocks the emulator for all other operations (input parsing,
mouse report generation, live rendering, OSC 52 extraction) and happens inside
a Tokio async task, blocking the worker thread.

### Changes

**Short-term (`emu.rs`):**

- Split `scroll_snapshot` into a locked section that copies the row range out,
  and an unlocked section that builds the `Frame` from the copy.
- `render_frame` already builds a `Frame`. Instead, copy the raw grid cells for
  the visible rows while locked, then serialise SGR outside the lock.

```rust
pub fn scroll_snapshot(&mut self, off: u32) -> (Frame, u32) {
    // Locked: scroll and copy raw cell data
    let cur = self.term.grid().display_offset() as i32;
    self.term.scroll_display(Scroll::Delta(off as i32 - cur));
    let achieved = self.term.grid().display_offset() as u32;

    let snapshot = self.capture_visible_cells(); // new fn: copies cells while locked

    self.term.scroll_display(Scroll::Bottom);

    // Unlocked: build Frame from snapshot
    let frame = Frame {
        lines: serialize_cells_to_sgr(&snapshot),
        cx: 0,
        cy: self.rows,
        cursor_shape: 0,
        cursor_blink: false,
        // ...other fields...
    };
    (frame, achieved)
}
```

**Longer-term (`session.rs`):**

- Move `scroll_capture` to `spawn_blocking` if rendering remains CPU-heavy
  (unlikely from the review's data).
- Consider a separate scrollback ring buffer in `SessionEmu` that supports
  indexed viewport extraction without mutating the alacritty display offset.

### Why so late

This is a performance optimisation, not a correctness fix. The earlier items
fix bugs that cause visible misbehaviour. This one makes scrollback *faster*
but doesn't change what the user sees.

---

## 10. Reduce full-frame scroll responses

### What

Every scroll offset change serialises and transmits all visible rows, then
the C# client reparses all SGR lines and rebuilds the render texture. At
20 requests/second for large panes this generates substantial allocation
and GPU churn.

### Changes

**`session.rs` — `scroll_capture`**:

- Cache `ScreenView` keyed by `(name, live_seq, off, cols, rows)`.
- Return cached copy when the key matches; invalidate on `live_seq` change.

**`TerminalWindow.cs`**:

- The existing `_cache` RenderTexture already caches the painted output
  across frames. As long as the same `ScreenBuf` (same name+seq+off) is
  returned, the blit path reuses the cache.
- No additional C# changes needed — the blit/miss logic already avoids
  repaints when `_cacheSeq`/`_cacheOff` match.

### Why last

The caching optimisation is only safe after:
- Request identity ensures we don't cache a stale response.
- The throttle fix reduces request volume.
- The coalescer fix prevents out-of-order frames from arriving and
  invalidating the cache at the wrong moment.

---

## Summary

| # | What | Where | Depends on |
|---|---|---|---|
| 1 | Request identity for scroll responses | `api.rs`, `session.rs`, `TerminalWindow.cs`, `SessionHub.cs` | — |
| 2 | Leading-edge scroll throttle | `TerminalWindow.cs` | 1 |
| 3 | Fix `Off == 0` gate | `TerminalWindow.cs` | 1 |
| 4 | Batch app-wheel reports | `TerminalWindow.cs`, `SessionHub.cs`, `api.rs`, `session.rs`, `emu.rs` | 2 |
| 5 | Fix WS coalescer ordering | `api.rs` | — |
| 6 | u16 → u32 scroll offsets | `api.rs`, `session.rs`, `emu.rs`, `tmux.rs` | 1 |
| 7 | Protect local scrollback from app mode change | `TerminalWindow.cs` | — |
| 8 | Suppress cursor on scrollback frames | `TerminalWindow.cs` | — |
| 9 | Reduce `scroll_snapshot` lock duration | `emu.rs` | 6 |
| 10 | Cache scroll responses | `session.rs` | 1, 2, 5 |

Items 1–8 are correctness fixes. Items 9–10 are performance.

About half the implementation work is in the daemon (Rust) and half in the mod
(C#). Every item has a clear test: the scroll gesture must track the wheel
smoothly, never jump backward, never forward wheel events into an app the user
isn't looking at, and never draw a blinking cursor on a history page.