use prost::Message;
use std::{hint::black_box, time::Instant};
#[expect(
    dead_code,
    reason = "only screen events are benchmarked from the full schema"
)]
#[expect(
    clippy::large_enum_variant,
    reason = "benchmark the generated wire types as emitted by prost"
)]
mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}
fn measure(
    name: &str,
    lane: &str,
    size: usize,
    mut action: impl FnMut() -> Result<(), prost::DecodeError>,
) -> Result<(), prost::DecodeError> {
    for _ in 0..300 {
        action()?;
    }
    let mut samples = [0.0; 21];
    for sample in &mut samples {
        let start = Instant::now();
        for _ in 0..1000 {
            action()?;
        }
        *sample = start.elapsed().as_secs_f64() * 1e6 / 1000.0;
    }
    samples.sort_by(f64::total_cmp);
    println!("{name},{lane},{size},{:.3},{:.3}", samples[10], samples[19]);
    Ok(())
}
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let dir = std::env::args().nth(1).ok_or("missing fixture directory")?;
    println!("fixture,lane,wire_bytes,p50_us,p95_us");
    for name in ["plain", "ansi", "unicode", "large"] {
        let binary = std::fs::read(format!("{dir}/{name}.pb"))?;
        let event = wire::Event::decode(binary.as_slice())?;
        if !matches!(event.payload, Some(wire::event::Payload::Screen(_))) {
            return Err(format!("{name}: expected a screen event").into());
        }
        let reencoded = event.encode_to_vec();
        std::fs::write(format!("{dir}/{name}.rust.pb"), &reencoded)?;
        if wire::Event::decode(reencoded.as_slice())? != event {
            return Err(format!("{name}: round-trip mismatch").into());
        }
        measure(name, "protobuf-encode", reencoded.len(), || {
            black_box(event.encode_to_vec());
            Ok(())
        })?;
        measure(name, "protobuf-decode", binary.len(), || {
            black_box(wire::Event::decode(binary.as_slice())?);
            Ok(())
        })?;
    }
    Ok(())
}
