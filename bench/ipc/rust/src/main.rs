use prost::Message;
use std::{hint::black_box, time::Instant};
#[allow(dead_code)]
mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}
fn measure(name: &str, lane: &str, size: usize, mut action: impl FnMut()) {
    for _ in 0..300 {
        action();
    }
    let mut samples = Vec::new();
    for _ in 0..21 {
        let start = Instant::now();
        for _ in 0..1000 {
            action();
        }
        samples.push(start.elapsed().as_secs_f64() * 1e6 / 1000.0);
    }
    samples.sort_by(f64::total_cmp);
    println!("{name},{lane},{size},{:.3},{:.3}", samples[10], samples[19]);
}
fn main() {
    let dir = std::env::args().nth(1).expect("fixture directory");
    println!("fixture,lane,wire_bytes,p50_us,p95_us");
    for name in ["plain", "ansi", "unicode", "large"] {
        let binary = std::fs::read(format!("{dir}/{name}.pb")).unwrap();
        let event = wire::Event::decode(binary.as_slice()).unwrap();
        assert!(matches!(
            event.payload,
            Some(wire::event::Payload::Screen(_))
        ));
        let reencoded = event.encode_to_vec();
        std::fs::write(format!("{dir}/{name}.rust.pb"), &reencoded).unwrap();
        assert_eq!(wire::Event::decode(reencoded.as_slice()).unwrap(), event);
        measure(name, "protobuf-encode", reencoded.len(), || {
            black_box(event.encode_to_vec());
        });
        measure(name, "protobuf-decode", binary.len(), || {
            black_box(wire::Event::decode(binary.as_slice()).unwrap());
        });
    }
}
