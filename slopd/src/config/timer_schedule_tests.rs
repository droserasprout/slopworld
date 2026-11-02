use super::Schedule;

#[test]
fn stepped_calendar_wildcards_preserve_the_other_day_constraint() {
    // 1970-01-01 is Thursday; January 5 is Monday.
    let monday = 4 * 1440 + 9 * 60;
    let schedule = Schedule::parse("0 9 */1 * 1").unwrap();
    assert!(!schedule.matches(9 * 60));
    assert!(schedule.matches(monday));
    assert_eq!(schedule.next_after(0), Some(monday));

    let schedule = Schedule::parse("0 0 2 * */1").unwrap();
    assert!(!schedule.matches(0));
    assert!(schedule.matches(1440));
    assert_eq!(schedule.next_after(0), Some(1440));
}

#[test]
fn stepped_calendar_wildcards_still_restrict_their_own_values() {
    // January 5 and 19 are odd Mondays; January 12 is an even Monday.
    let schedule = Schedule::parse("0 0 */2 * 1").unwrap();
    assert!(!schedule.matches(0));
    assert!(schedule.matches(4 * 1440));
    assert!(!schedule.matches(11 * 1440));
    assert_eq!(schedule.next_after(4 * 1440), Some(18 * 1440));

    let schedule = Schedule::parse("0 0 2 * */2").unwrap();
    assert!(!schedule.matches(0));
    assert!(!schedule.matches(1440));
}
