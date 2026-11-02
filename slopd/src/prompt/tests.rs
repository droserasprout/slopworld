use super::*;

#[test]
fn magic_8_ball_draws_answers_without_client_tips_or_session_context() {
    let rendered = render_prompt("{{ magic_8_ball }}\n{{magic_8_ball}}", &[]);
    let answers: Vec<_> = rendered.lines().collect();
    assert_eq!(answers.len(), 2);
    for answer in answers {
        assert!(
            MAGIC_8_BALL_ANSWERS.lines().any(|line| line == answer),
            "{answer}"
        );
    }
}

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
fn unknown_and_unclosed_placeholders_are_left_alone() {
    let text = "- {{ random_tip }} and {{ whatever }}";
    assert_eq!(render_prompt("{{ whatever }}", &[]), "{{ whatever }}");

    let tips = vec!["a tip".to_string()];
    assert_eq!(render_prompt(text, &tips), "- a tip and {{ whatever }}");

    assert_eq!(
        render_prompt("keep {{ random_tip", &tips),
        "keep {{ random_tip"
    );
}

#[test]
fn prompt_context_variables_render_and_unknown_variables_survive() {
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

#[test]
fn unattended_prompts_resolve_distinct_tips_from_the_shared_catalog() {
    let cfg = Config::default();
    let session = crate::config::SessionCfg::default();
    let project = crate::config::ProjectCfg::default();
    let rendered = prepare_prompt(&cfg, &session, &project, "Useful tips", &[]).unwrap();
    assert!(!rendered.contains("{{"));
    let text = ["{{ random_tip }}"; 5].join("\n");
    let rendered = render_prompt(&text, &[]);
    let tips: std::collections::HashSet<_> = rendered.lines().collect();
    assert_eq!(tips.len(), 5);
    for tip in tips {
        assert!(
            include_str!("../../../shared/loading-tips.txt")
                .lines()
                .any(|line| line == tip)
        );
        assert!(!tip.ends_with(" ("));
        assert!(!tip.starts_with('#'));
    }
}
