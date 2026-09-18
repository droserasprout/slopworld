//! Deterministic, game-free daemon performance benchmark.

use std::hint::black_box;
use std::time::Instant;

use anyhow::Result;

use crate::emu::{benchmark_content_hash as hash_content, Frame, SessionEmu};
use crate::perf;
use crate::session::{Event, EventMessage, ScreenView};

const COLS: u16 = 120;
const ROWS: u16 = 34;
const WARMUP: usize = 40;
const SAMPLES: usize = 200;

pub(crate) fn run() -> Result<()> {
    println!("SlopWorld daemon benchmark ({COLS}x{ROWS}, {SAMPLES} samples)");
    println!("timings are microseconds; warmup={WARMUP}");

    benchmark_render_cases();
    benchmark_ansi_strip();
    benchmark_content_hash();
    benchmark_websocket_serialization();

    if let Some(line) = perf::report() {
        println!("{line}");
    }
    Ok(())
}

fn benchmark_render_cases() {
    benchmark_render(
        "render no-output",
        seeded(),
        |_emu, _| {},
        |emu| emu.render(),
    );
    benchmark_render(
        "render cursor-only",
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
        "render one-row-edit",
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
        "render full-redraw",
        seeded(),
        |emu, sample| emu.feed(&full_redraw(sample % 2 == 0)),
        |emu| emu.render(),
    );
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

fn benchmark_ansi_strip() {
    let lines = (0..ROWS)
        .map(|row| format!("\x1b[38;5;{}mrow {row:02} prompt\x1b[0m", row % 16))
        .collect::<Vec<_>>();
    measure("ansi-strip tail", |_| {
        crate::session::strip_sgr_tail(&lines, 12).len()
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
    measure("websocket-json fresh screen", |_| {
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

    let mut timings = Vec::with_capacity(SAMPLES);
    for sample in 0..SAMPLES {
        let started = Instant::now();
        black_box(action(sample));
        timings.push(started.elapsed().as_nanos());
    }
    timings.sort_unstable();
    println!(
        "{name:<44} p50={:.2} p95={:.2}",
        nanos_us(timings[(timings.len() - 1) * 50 / 100]),
        nanos_us(timings[(timings.len() - 1) * 95 / 100]),
    );
}

fn nanos_us(nanos: u128) -> f64 {
    nanos as f64 / 1_000.0
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
