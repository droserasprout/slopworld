//! Deterministic, game-free daemon performance benchmark.

use std::hint::black_box;
use std::time::{Duration, Instant};

use anyhow::Result;

use crate::emu::{benchmark_content_hash as hash_content, Frame, SessionEmu};
use crate::perf;
use crate::session::{Event, EventMessage, ScreenView};

mod storage;

const COLS: u16 = 120;
const ROWS: u16 = 34;
const WARMUP: usize = 40;
const SAMPLES: usize = 50;
const MIN_BATCH: Duration = Duration::from_millis(2);
const MAX_BATCH: usize = 65_536;

pub(crate) fn run() -> Result<()> {
    println!("SlopWorld daemon benchmark ({COLS}x{ROWS}, {SAMPLES} samples)");
    println!("Timings are microseconds per operation in warmed batches. Warmup: {WARMUP}");

    benchmark_render_cases();
    benchmark_history_padding();
    benchmark_content_hash();
    benchmark_websocket_serialization();
    storage::run()?;

    if let Some(line) = perf::report() {
        println!("{line}");
    }
    Ok(())
}

fn benchmark_render_cases() {
    benchmark_render(
        "feed-and-render no-output",
        seeded(),
        |_emu, _| {},
        |emu| emu.render(),
    );
    benchmark_render(
        "feed-and-render cursor-only",
        seeded(),
        |emu, sample| {
            let position = if sample % 2 == 0 {
                b"\x1b[1;1H".as_slice()
            } else {
                b"\x1b[1;2H".as_slice()
            };
            emu.feed(position);
        },
        |emu| emu.render(),
    );
    benchmark_render(
        "feed-and-render one-row-edit",
        seeded(),
        |emu, sample| {
            let row = if sample % 2 == 0 {
                b"\x1b[10;1H\x1b[31mrow edit A\x1b[0m"
            } else {
                b"\x1b[10;1H\x1b[32mrow edit B\x1b[0m"
            };
            emu.feed(row);
        },
        |emu| emu.render(),
    );
    benchmark_render(
        "feed-and-render full-redraw",
        seeded(),
        |emu, sample| emu.feed(&full_redraw(sample % 2 == 0)),
        |emu| emu.render(),
    );
}

fn benchmark_history_padding() {
    // Clients do not show leading blank history, but the VT grid retains it. Exercise
    // cursor damage after scrolling it in: the ordinary seeded fixture has no history.
    for (count, blank) in [
        (0, true),
        (100, true),
        (1_000, true),
        (10_000, true),
        (10_000, false),
    ] {
        let mut emu = SessionEmu::new(COLS, ROWS);
        // Make the nonblank control's oldest row nonblank too. Otherwise moving the cursor
        // straight to the bottom seeds an unrelated blank prefix in that control.
        if !blank {
            emu.feed(b"history");
        }
        emu.feed(format!("\x1b[{ROWS};1H").as_bytes());
        let line = if blank { "\r\n" } else { "history\r\n" };
        emu.feed(line.repeat(count).as_bytes());
        let frame = emu.render();
        if blank {
            assert_eq!(frame.history, 0);
        } else {
            assert_eq!(frame.history, count as u32);
        }
        benchmark_render(
            &format!(
                "feed-and-render cursor history={count} {}",
                if blank { "blank" } else { "text" }
            ),
            emu,
            |emu, sample| {
                emu.feed(if sample % 2 == 0 {
                    b"\x1b[1;1H"
                } else {
                    b"\x1b[1;2H"
                });
            },
            |emu| emu.render(),
        );
    }
}

fn benchmark_render<P, A>(name: &str, mut emu: SessionEmu, mut prepare: P, mut action: A)
where
    P: FnMut(&mut SessionEmu, usize),
    A: FnMut(&mut SessionEmu) -> Frame,
{
    measure(name, |sample| {
        prepare(&mut emu, sample);
        action(&mut emu).content_hash as usize
    });
}

fn benchmark_content_hash() {
    let frame = seeded_frame();
    measure("frame-hash rows", |_| benchmark_content_hash_rows(&frame));
}

fn benchmark_content_hash_rows(frame: &Frame) -> usize {
    hash_content(&frame.lines) as usize
}

fn benchmark_websocket_serialization() {
    let screen = screen_view("bench-fresh", &seeded_frame());
    measure("websocket-protobuf fresh screen", |_| {
        EventMessage::new(Event::Screen {
            screen: screen.clone(),
        })
        .encoded()
        .expect("benchmark event")
        .len()
    });
    for sessions in [1usize, 4, 8] {
        let events = (0..sessions)
            .map(|index| {
                EventMessage::new(Event::Screen {
                    screen: screen_view(&format!("bench-{index}"), &seeded_frame()),
                })
            })
            .collect::<Vec<_>>();
        for clients in [1usize, 4, 8] {
            measure(
                &format!("websocket-cached sessions={sessions} clients={clients}"),
                |_| {
                    let mut bytes = 0usize;
                    for _ in 0..clients {
                        for event in &events {
                            let encoded = event.encoded().expect("benchmark event");
                            perf::count("websocket-bytes", encoded.len() as u64);
                            bytes += encoded.len();
                        }
                    }
                    bytes
                },
            );
        }
    }
}

fn measure<F>(name: &str, mut action: F)
where
    F: FnMut(usize) -> usize,
{
    for sample in 0..WARMUP {
        black_box(action(sample));
    }

    // Keep the operation index advancing across batches: alternating edits must not
    // become repeated no-ops when calibration changes the batch size.
    let mut operation = WARMUP;
    let mut batch = 1;
    loop {
        let started = Instant::now();
        for _ in 0..batch {
            black_box(action(operation));
            operation += 1;
        }
        if started.elapsed() >= MIN_BATCH || batch == MAX_BATCH {
            break;
        }
        batch *= 2;
    }

    let mut timings = Vec::with_capacity(SAMPLES);
    for _ in 0..SAMPLES {
        let started = Instant::now();
        for _ in 0..batch {
            black_box(action(operation));
            operation += 1;
        }
        timings.push(started.elapsed().as_secs_f64() * 1e6 / batch as f64);
    }
    timings.sort_by(f64::total_cmp);
    println!(
        "{name:<44} p50={:.3} p95={:.3}",
        timings
            .get((timings.len() - 1) * 50 / 100)
            .copied()
            .expect("benchmark samples are nonempty"),
        timings
            .get((timings.len() - 1) * 95 / 100)
            .copied()
            .expect("benchmark samples are nonempty"),
    );
}

fn seeded() -> SessionEmu {
    let mut emu = SessionEmu::new(COLS, ROWS);
    let mut screen = Vec::new();
    screen.extend_from_slice(b"\x1b[2J\x1b[H");
    for row in 0..ROWS {
        screen.extend_from_slice(format!("\x1b[{};1Hrow {row:02} baseline", row + 1).as_bytes());
    }
    emu.feed(&screen);
    black_box(emu.render());
    emu
}

fn seeded_frame() -> Frame {
    let mut emu = seeded();
    emu.render()
}

fn full_redraw(first: bool) -> Vec<u8> {
    let mut screen = Vec::new();
    screen.extend_from_slice(b"\x1b[2J\x1b[H");
    for row in 0..ROWS {
        let text = if first {
            "full redraw A"
        } else {
            "full redraw B"
        };
        screen.extend_from_slice(format!("\x1b[{};1H\x1b[34m{text}\x1b[0m", row + 1).as_bytes());
    }
    screen
}

fn screen_view(name: &str, frame: &Frame) -> ScreenView {
    ScreenView {
        input_timings: Vec::new(),
        name: name.to_string(),
        seq: 1,
        cols: COLS,
        rows: ROWS,
        cx: frame.cx,
        cy: frame.cy,
        off: 0,
        history: frame.history,
        cursor_shape: frame.cursor_shape,
        cursor_blink: frame.cursor_blink,
        app_mouse: frame.app_mouse,
        app_drag: frame.app_drag,
        alt_screen: frame.alt_screen,
        title: frame.title.clone(),
        request_id: 0,
        lines: frame.lines.clone(),
    }
}
