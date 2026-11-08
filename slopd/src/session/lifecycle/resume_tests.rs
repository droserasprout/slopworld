use super::*;

#[test]
fn supported_commands_discover_only_their_provider_transcripts() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let providers = [
        ("codex", "home/.codex/sessions"),
        ("claude", "home/.claude/projects"),
        ("pi", "home/.pi/agent/sessions"),
    ];
    for (provider, relative) in providers {
        let session = SessionCfg::default();
        let root = crate::sandbox::state_dir(&session).unwrap();
        assert!(!has_history(provider, &session));
        let directory = root.join(relative).join("project/date");
        std::fs::create_dir_all(&directory).unwrap();
        let transcript = directory.join("conversation.jsonl");
        std::fs::write(&transcript, "{}\n").unwrap();

        for (command, _) in providers {
            assert_eq!(has_history(command, &session), command == provider);
        }
        for command in [
            format!("/opt/agents/{provider} --model 'a model'"),
            format!("'/opt/agent tools/{provider}' --flag"),
        ] {
            assert!(has_history(&command, &session), "{command}");
        }
        std::fs::remove_file(transcript).unwrap();
        assert!(!has_history(provider, &session));
    }
}

#[test]
fn unknown_empty_and_malformed_commands_start_fresh_despite_history() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let session = SessionCfg::default();
    let root = crate::sandbox::state_dir(&session).unwrap();
    for relative in [
        "home/.codex/sessions",
        "home/.claude/projects",
        "home/.pi/agent/sessions",
    ] {
        let directory = root.join(relative);
        std::fs::create_dir_all(&directory).unwrap();
        std::fs::write(directory.join("conversation.jsonl"), "{}\n").unwrap();
    }
    assert!(has_history("codex", &session));
    for command in [
        "",
        "  \t\n",
        "''",
        "/",
        "other-agent",
        "Codex",
        "codex-wrapper",
        "env codex",
        "sh -c codex",
        "codex 'unfinished",
        "claude \"unfinished",
    ] {
        assert!(!has_history(command, &session), "{command:?}");
    }
}

#[test]
fn history_follows_private_identity_across_renames_and_rejects_invalid_identity() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let mut session = SessionCfg {
        name: "original".into(),
        ..Default::default()
    };
    let directory = crate::sandbox::state_dir(&session)
        .unwrap()
        .join("home/.codex/sessions");
    std::fs::create_dir_all(&directory).unwrap();
    std::fs::write(directory.join("conversation.jsonl"), "{}\n").unwrap();
    session.name = "renamed".into();
    assert!(has_history("codex", &session));
    let replacement = SessionCfg {
        name: session.name.clone(),
        ..Default::default()
    };
    assert!(!has_history("codex", &replacement));
    for state_id in ["", "../escape", "/absolute", "invalid"] {
        session.state_id = state_id.into();
        assert!(!has_history("codex", &session), "{state_id:?}");
    }
}

#[test]
fn missing_empty_and_deleted_history_start_fresh() {
    let root = crate::test_support::fixture("resume");
    let path = root.join("sessions");
    assert!(!conversation_files(&path, 0, &mut 100));
    std::fs::create_dir(&path).unwrap();
    std::fs::write(path.join("empty.jsonl"), "").unwrap();
    std::fs::write(path.join("config.json"), "{}").unwrap();
    assert!(!conversation_files(&path, 0, &mut 100));
    std::fs::write(path.join("conversation.jsonl"), "{}\n").unwrap();
    assert!(conversation_files(&path, 0, &mut 100));
    std::fs::remove_file(path.join("conversation.jsonl")).unwrap();
    assert!(!conversation_files(&path, 0, &mut 100));
}

#[test]
fn transcript_links_are_not_followed() {
    let root = crate::test_support::fixture("resume-links");
    std::fs::write(root.join("outside.jsonl"), "{}\n").unwrap();
    let path = root.join("sessions");
    std::fs::create_dir(&path).unwrap();
    std::os::unix::fs::symlink(root.join("outside.jsonl"), path.join("link.jsonl")).unwrap();
    assert!(!conversation_files(&path, 0, &mut 100));
}

#[test]
fn directory_and_dangling_links_do_not_supply_history() {
    let root = crate::test_support::fixture("resume-directory-links");
    let outside = root.join("outside");
    std::fs::create_dir(&outside).unwrap();
    std::fs::write(outside.join("conversation.jsonl"), "{}\n").unwrap();
    let path = root.join("sessions");
    std::fs::create_dir(&path).unwrap();
    let directory_link = path.join("linked-directory");
    std::os::unix::fs::symlink(&outside, &directory_link).unwrap();
    std::os::unix::fs::symlink(root.join("missing"), path.join("dangling.jsonl")).unwrap();
    assert!(!conversation_files(&directory_link, 0, &mut 100));
    assert!(!conversation_files(&path, 0, &mut 100));
    // Ignored links must not prevent a later real transcript from being found.
    std::fs::write(path.join("conversation.jsonl"), "{}\n").unwrap();
    assert!(conversation_files(&path, 0, &mut 100));
}

#[test]
fn only_nonempty_jsonl_files_count_as_transcripts() {
    let root = crate::test_support::fixture("resume-file-types");
    for name in [
        "config.json",
        "conversation.JSONL",
        "jsonl",
        "conversation.jsonl.bak",
    ] {
        std::fs::write(root.join(name), "{}\n").unwrap();
    }
    std::fs::create_dir(root.join("directory.jsonl")).unwrap();
    std::fs::write(root.join("empty.jsonl"), "").unwrap();
    assert!(!conversation_files(&root, 0, &mut 100));
    // A directory root is required even if the supplied path is a transcript.
    let transcript = root.join("conversation.jsonl");
    std::fs::write(&transcript, "{}\n").unwrap();
    assert!(!conversation_files(&transcript, 0, &mut 100));
    assert!(!conversation_files(&transcript.join("child"), 0, &mut 100));
    assert!(conversation_files(&root, 0, &mut 100));
}

#[test]
fn traversal_includes_depth_eight_but_does_not_enter_depth_nine() {
    let root = crate::test_support::fixture("resume-depth");
    let mut directory = root.to_path_buf();
    for _ in 0..8 {
        directory.push("nested");
    }
    std::fs::create_dir_all(&directory).unwrap();
    let transcript = directory.join("conversation.jsonl");
    std::fs::write(&transcript, "{}\n").unwrap();
    assert!(conversation_files(&root, 0, &mut 100));
    let too_deep = directory.join("nested");
    std::fs::create_dir(&too_deep).unwrap();
    std::fs::rename(transcript, too_deep.join("conversation.jsonl")).unwrap();
    assert!(!conversation_files(&root, 0, &mut 100));
}

#[test]
fn traversal_budget_counts_directories_and_accepts_the_last_allowed_entry() {
    let root = crate::test_support::fixture("resume-budget");
    let directory = root.join("one/two");
    std::fs::create_dir_all(&directory).unwrap();
    std::fs::write(directory.join("conversation.jsonl"), "{}\n").unwrap();
    for budget in 0..=3 {
        let mut remaining = budget;
        assert_eq!(conversation_files(&root, 0, &mut remaining), budget == 3);
        assert_eq!(remaining, 0);
    }
}

#[test]
fn traversal_budget_is_shared_across_sibling_directories() {
    let root = crate::test_support::fixture("resume-shared-budget");
    // Either iteration order spends two entries on each subtree. No transcript
    // matches, so the observed cost covers both siblings without ordering races.
    for name in ["one", "two"] {
        let directory = root.join(name);
        std::fs::create_dir(&directory).unwrap();
        std::fs::write(directory.join("config.json"), "{}").unwrap();
    }
    for (budget, expected_remaining) in [(2, 0), (3, 0), (5, 1)] {
        let mut remaining = budget;
        assert!(!conversation_files(&root, 0, &mut remaining));
        assert_eq!(remaining, expected_remaining);
    }
}
