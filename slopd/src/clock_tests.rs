use super::*;
use std::time::Duration;

#[test]
fn duration_observations_keep_their_requested_units() {
    let duration = Duration::from_micros(1_234_567);
    assert_eq!(duration_ms(duration), 1_234);
    assert_eq!(duration_us(duration), 1_234_567);
    assert_eq!(duration_ms(Duration::ZERO), 0);
    assert_eq!(duration_us(Duration::ZERO), 0);
}

#[test]
fn duration_observations_saturate_before_wire_integer_overflow() {
    assert_eq!(duration_ms(Duration::MAX), u64::MAX);
    assert_eq!(duration_us(Duration::MAX), u64::MAX);
    assert_eq!(duration_ms(Duration::from_millis(u64::MAX)), u64::MAX);
    assert_eq!(duration_us(Duration::from_micros(u64::MAX)), u64::MAX);
}
