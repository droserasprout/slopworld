use super::*;

#[test]
fn every_tip_mention_is_its_own_draw() {
    let tips: Vec<String> = ["one", "two", "three", "four", "five"]
        .iter()
        .map(|s| s.to_string())
        .collect();
    let out = render_prompt(
        "- {{ random_tip }}\n- {{random_tip}}\n- {{ random_tip}}\n\
             - {{ random_tip }}\n- {{ random_tip }}",
        &tips,
    );
    for tip in &tips {
        assert_eq!(out.matches(tip.as_str()).count(), 1, "{tip} exactly once");
    }

    let short = render_prompt(
        "{{ random_tip }}/{{ random_tip }}/{{ random_tip }}",
        &tips[..2],
    );
    assert_eq!(short, "one/two/one");
}

#[test]
fn a_template_with_nothing_to_fill_it_is_left_alone() {
    let text = "- {{ random_tip }} and {{ whatever }}";
    assert_eq!(render_prompt(text, &[]), text);

    let tips = vec!["a tip".to_string()];
    assert_eq!(render_prompt(text, &tips), "- a tip and {{ whatever }}");

    assert_eq!(
        render_prompt("keep {{ random_tip", &tips),
        "keep {{ random_tip"
    );
}

#[test]
fn breadcrumb_context_variables_render_and_unknown_variables_survive() {
    let vars = PromptVars {
        agent: "Ada",
        project: "slopworld",
        directory: "/src/slopworld",
        command: "codex",
    };
    assert_eq!(
        render_prompt_with(
            "{{ agent }} in {{ project }} at {{ directory }} via {{ command }}; {{ later }}",
            &[],
            Some(&vars),
        ),
        "Ada in slopworld at /src/slopworld via codex; {{ later }}"
    );
}
