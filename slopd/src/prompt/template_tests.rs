use super::*;

#[test]
fn unresolved_and_unfinished_placeholders_survive() {
    assert_eq!(
        render(
            "{{ known }} / {{ unknown }} / {{ known }} / {{ unfinished",
            |key| { (key == "known").then_some("value") }
        ),
        "value / {{ unknown }} / value / {{ unfinished"
    );
}

#[test]
fn replacements_are_literal_and_each_occurrence_is_resolved() {
    let mut calls = 0;
    let output = render("{{key}}{{ key }}", |_| {
        calls += 1;
        Some("{{another}}")
    });
    assert_eq!(calls, 2);
    assert_eq!(output, "{{another}}{{another}}");
}
