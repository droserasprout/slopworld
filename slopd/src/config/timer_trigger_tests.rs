use super::*;

fn calendar(schedule: &str, timezone: &str) -> Trigger {
    Trigger::parse(&TimerCfg {
        trigger: "calendar".into(),
        schedule: schedule.into(),
        timezone: timezone.into(),
        ..Default::default()
    })
    .unwrap()
}
fn stamp(text: &str) -> u64 {
    text.parse::<Timestamp>()
        .unwrap()
        .as_second()
        .try_into()
        .unwrap()
}

#[test]
fn named_zones_follow_dst_and_weekdays() {
    let schedule = calendar("Mon..Fri 09:00", "America/New_York");
    assert_eq!(
        schedule.next_calendar(stamp("2026-03-06T15:00:00Z")),
        Some(stamp("2026-03-09T13:00:00Z"))
    );
    assert_eq!(
        schedule.next_calendar(stamp("2026-10-30T14:00:00Z")),
        Some(stamp("2026-11-02T14:00:00Z"))
    );
    assert_eq!(
        calendar("Mon,Wed,Fri 09:00", "Asia/Kathmandu")
            .next_calendar(stamp("2026-11-02T00:00:00Z")),
        Some(stamp("2026-11-02T03:15:00Z"))
    );
}
#[test]
fn missing_local_times_skip_and_repeated_times_fire_once() {
    let spring = calendar("02:30", "America/New_York");
    assert_eq!(
        spring.next_calendar(stamp("2026-03-08T00:00:00Z")),
        Some(stamp("2026-03-09T06:30:00Z"))
    );
    let fall = calendar("01:30", "America/New_York");
    let first = stamp("2026-11-01T05:30:00Z");
    assert_eq!(
        fall.next_calendar(stamp("2026-11-01T00:00:00Z")),
        Some(first)
    );
    assert_eq!(
        fall.next_calendar(first),
        Some(stamp("2026-11-02T06:30:00Z"))
    );
}
#[test]
fn durations_are_bounded_and_require_units() {
    for (text, seconds) in [("1h 30min", 5400), ("2min5s", 125), ("1 d", 86400)] {
        assert_eq!(duration(text).unwrap(), seconds);
    }
    for text in [
        "",
        "0s",
        "-5s",
        "1.5h",
        "100",
        "367d",
        "18446744073709551615d",
        "1fortnight",
    ] {
        assert!(duration(text).is_err(), "{text}");
    }
    for (schedule, zone) in [
        ("Fri..Mon 09:00", "UTC"),
        ("09:00", "auto"),
        ("09:00", "No/Such_Zone"),
        ("25:00", "UTC"),
    ] {
        assert!(
            Trigger::parse(&TimerCfg {
                trigger: "calendar".into(),
                schedule: schedule.into(),
                timezone: zone.into(),
                ..Default::default()
            })
            .is_err()
        );
    }
}
#[test]
fn preview_uses_activation_and_startup_anchors() {
    assert_eq!(Trigger::Interval(10).preview(100, 50), vec![110, 120, 130]);
    assert_eq!(Trigger::Active(10).preview(100, 50), vec![110]);
    assert_eq!(Trigger::Startup(10).preview(100, 50), vec![100]);
    assert_eq!(Trigger::Startup(60).preview(100, 50), vec![110]);
}
