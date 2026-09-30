use super::*;

#[test]
fn codex_composer_recovers_the_submitted_prompt() {
    let mut c = Composer::ready();
    c.literal("fix teh parser");
    for _ in 0..8 {
        c.key("Left");
    }
    c.key("BSpace");
    c.literal("he");
    c.key("DC");
    let Some(Submission::Prompt(prompt)) = c.key("Enter") else {
        panic!("expected a prompt")
    };
    assert_eq!(prompt, "fix the parser");
}

#[test]
fn codex_composer_rearms_new_and_skips_uncertain_input() {
    let mut c = Composer::ready();
    c.literal("/new parser work");
    let Some(Submission::New(name)) = c.key("Enter") else {
        panic!("expected a boundary")
    };
    assert_eq!(name.as_deref(), Some("parser work"));

    c.literal("history entry");
    c.key("Up");
    assert!(c.key("Enter").is_none());
    c.literal("fresh prompt");
    assert!(matches!(c.key("Enter"), Some(Submission::Prompt(prompt)) if prompt == "fresh prompt"));
}

#[test]
fn codex_composer_handles_common_controls_and_resynchronizes() {
    let mut c = Composer::ready();
    c.literal("fix parser");
    c.key("Home");
    c.key("C-f");
    c.key("C-d");
    c.literal("i");
    let Some(Submission::Prompt(prompt)) = c.key("C-j") else {
        panic!("expected Ctrl-J to submit")
    };
    assert_eq!(prompt, "fix parser");

    c.literal("stale input");
    c.key("Escape");
    c.literal("replacement");
    assert!(c.key("Enter").is_none());
    c.literal("fresh prompt");
    assert!(matches!(c.key("Enter"), Some(Submission::Prompt(prompt)) if prompt == "fresh prompt"));
}

#[test]
fn dialog_answers_are_not_prompt_titles() {
    assert!(is_dialog_answer(" yes "));
    assert!(is_dialog_answer("1"));
    assert!(!is_dialog_answer("fix the parser"));
}

#[test]
fn prompt_minimum_counts_unicode_characters() {
    assert!(prompt_is_long_enough("commit", 0));
    assert!(prompt_is_long_enough("12345678901234567890", 20));
    assert!(prompt_is_long_enough("áéíóú", 5));
    assert!(!prompt_is_long_enough("commit", 20));
}

#[test]
fn title_agents_cover_presets_and_explicit_commands() {
    let mut cfg = Config::default();
    cfg.daemon.title_model = "shared-title".into();
    cfg.daemon.pi_titles = TitlePolicy::Always;

    let codex = SessionCfg {
        cmd: Some("codex --yolo".into()),
        ..Default::default()
    };
    assert!(matches!(title_agent(&cfg, &codex), Some(TitleAgent::Codex)));

    let pi = SessionCfg {
        cmd: Some("pi --model test".into()),
        ..Default::default()
    };
    assert!(matches!(title_agent(&cfg, &pi), Some(TitleAgent::Pi)));
    assert_eq!(
        TitleSettings::for_session(&cfg, &pi, false).unwrap().model,
        "shared-title"
    );

    let labeled = SessionCfg {
        label: Some("keep this name".into()),
        cmd: Some("pi --model test".into()),
        ..Default::default()
    };
    assert!(TitleSettings::for_session(&cfg, &labeled, false).is_none());

    let host = SessionCfg {
        cmd: Some("bash".into()),
        ..Default::default()
    };
    assert!(TitleSettings::for_session(&cfg, &host, true).is_none());

    let other = SessionCfg {
        cmd: Some("opencode".into()),
        ..Default::default()
    };
    assert!(title_agent(&cfg, &other).is_none());
}

#[test]
fn title_submission_reports_uncertain_editing_without_a_submission() {
    let mut composer = Composer::ready();
    let keys = vec!["Up".to_string()];
    let (submission, uncertain) = build_title_submission(&mut composer, &keys, false);
    assert!(submission.is_none());
    assert!(uncertain);
}

#[test]
fn title_submission_uses_all_literal_chunks_before_enter() {
    let mut composer = Composer::ready();
    let chunks = vec!["fix ".to_string(), "the parser".to_string()];
    assert!(build_title_submission(&mut composer, &chunks, true)
        .0
        .is_none());

    let enter = vec!["Enter".to_string()];
    let (submission, uncertain) = build_title_submission(&mut composer, &enter, false);
    let Some(Submission::Prompt(prompt)) = submission else {
        panic!("expected submitted prompt")
    };
    assert_eq!(prompt, "fix the parser");
    assert!(!uncertain);
}

fn settings(policy: TitlePolicy) -> TitleSettings {
    TitleSettings {
        policy,
        minimum: 0,
        model: "test-model".into(),
        key_file: String::new(),
        summary_prompt: "Summarize".into(),
    }
}

fn request(capture: &mut TitleCapture, settings: &TitleSettings, prompt: &str) -> TitleRequest {
    capture.paste(prompt);
    let Some(TitleAction::Request(request)) =
        capture.capture_keys(settings, &["Enter".into()], false)
    else {
        panic!("expected request")
    };
    request
}

#[test]
fn once_attempt_is_consumed_on_failure_and_rearmed_only_by_new_conversation() {
    let settings = settings(TitlePolicy::Once);
    let mut capture = TitleCapture::default();
    let first = request(&mut capture, &settings, "Fix the parser");
    capture.paste("Another prompt");
    assert!(capture
        .capture_keys(&settings, &["Enter".into()], false)
        .is_none());
    assert!(!capture.finish(&first, None));
    capture.disable();
    capture.paste("Still the same conversation");
    assert!(capture
        .capture_keys(&settings, &["Enter".into()], false)
        .is_none());
    capture.paste("/new");
    assert!(matches!(
        capture.capture_keys(&settings, &["Enter".into()], false),
        Some(TitleAction::Boundary)
    ));
    let next = request(&mut capture, &settings, "A fresh conversation");
    assert!(capture.accepts(&next));
    assert!(!capture.accepts(&first));
}

#[test]
fn approval_answers_never_consume_the_once_attempt_even_with_zero_minimum() {
    let settings = settings(TitlePolicy::Once);
    let mut capture = TitleCapture::default();
    for answer in ["yes", "1", "okay", "cancel"] {
        capture.paste(answer);
        assert!(capture
            .capture_keys(&settings, &["Enter".into()], false)
            .is_none());
    }
    let next = request(&mut capture, &settings, "Fix the parser");
    assert!(capture.accepts(&next));
}

#[test]
fn stale_and_repeated_results_cannot_replace_a_title_or_clear_new_work() {
    let settings = settings(TitlePolicy::Always);
    let mut capture = TitleCapture::default();
    let old = request(&mut capture, &settings, "Old work");
    let new = request(&mut capture, &settings, "New work");
    assert!(!capture.finish(&old, Some("Stale".into())));
    assert!(capture.accepts(&new));
    assert!(capture.finish(&new, Some("Current".into())));
    assert!(!capture.finish(&new, Some("Repeated".into())));
    assert_eq!(capture.title(), Some("Current"));
    capture.reset();
    let replacement = request(&mut capture, &settings, "Replacement work");
    assert!(!capture.finish(&old, Some("Old process".into())));
    assert!(capture.accepts(&replacement));
}

#[test]
fn restored_title_consumes_once_and_label_edits_cancel_pending_work() {
    let mut capture = TitleCapture::restored(Some("Restored".into()));
    capture.paste("More work");
    assert!(capture
        .capture_keys(&settings(TitlePolicy::Once), &["Enter".into()], false)
        .is_none());
    let pending = request(&mut capture, &settings(TitlePolicy::Always), "New work");
    capture.label_changed(false);
    assert!(!capture.accepts(&pending));
    assert_eq!(capture.title(), Some("Restored"));
    capture.label_changed(true);
    assert!(capture.title().is_none());
}

#[test]
fn editing_boundaries_preserve_the_prompt() {
    for key in ["Left", "C-b", "BSpace", "C-h", "Right", "C-f", "DC", "C-d"] {
        let mut c = Composer::ready();
        c.literal("fresh prompt");
        if matches!(key, "Left" | "C-b" | "BSpace" | "C-h") {
            c.key("Home");
        }
        c.key(key);
        assert!(
            matches!(c.key("Enter"), Some(Submission::Prompt(prompt)) if prompt == "fresh prompt"),
            "{key}"
        );
    }
    let mut c = Composer::ready();
    c.key("C-d");
    c.literal("uncertain");
    assert!(c.key("Enter").is_none());
}
