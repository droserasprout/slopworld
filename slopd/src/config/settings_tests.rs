use super::*;
use crate::config::{
    Config, HostTerminalCfg, LibraryItemCfg, ProjectCfg, SessionCfg, TOKEN_REDACTED,
};

fn workspace() -> Config {
    let mut cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "agent".into(),
            ..Default::default()
        }],
        host_terminals: vec![HostTerminalCfg {
            name: "shell".into(),
            ..Default::default()
        }],
        library: vec![LibraryItemCfg {
            name: "prompt".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    cfg.daemon.token = "secret".into();
    cfg
}

#[test]
fn settings_serialization_excludes_workspace_and_runtime_serialization_stays_flat() {
    let cfg = workspace();
    let root = toml::Value::try_from(&cfg.settings).unwrap();
    for key in ["project", "session", "host_terminal", "library"] {
        assert!(root.get(key).is_none(), "{key}");
    }
    let view = toml::Value::try_from(&cfg).unwrap();
    assert_eq!(view.get("daemon"), root.get("daemon"));
    assert!(view.get("settings").is_none());
    assert_eq!(view["session"][0]["name"].as_str(), Some("agent"));
}

#[test]
fn root_replacement_retains_workspace_and_redacted_secret() {
    let old = workspace();
    let prepared = document::replace(&old, &format!(
        "[extension]\nnote = 'kept'\n[daemon]\nbind = '127.0.0.1:7717'\ntoken = '{TOKEN_REDACTED}'\n"
    )).unwrap();
    assert_eq!(prepared.candidate.daemon.token, "secret");
    assert_eq!(
        prepared.candidate.sessions[0].state_id,
        old.sessions[0].state_id
    );
    assert_eq!(prepared.candidate.projects[0].name, "repo");
    assert_eq!(prepared.candidate.host_terminals[0].name, "shell");
    assert_eq!(prepared.candidate.library[0].name, "prompt");
    let persisted: toml::Value = toml::from_str(&prepared.text).unwrap();
    assert_eq!(persisted["extension"]["note"].as_str(), Some("kept"));
    assert_eq!(persisted["daemon"]["token"].as_str(), Some("secret"));
    assert!(persisted.get("session").is_none());
    assert_eq!(old.daemon.token, "secret");
}

#[test]
fn patch_preserves_omission_and_applies_explicit_empty_false_and_zero() {
    let old = workspace();
    let text = "[daemon]\nbind = '127.0.0.1:7717'\ntoken = 'secret'\ntitle_min_chars = 9\nworker_templates = ['review']\n[daemon.usage_items.claude]\npoll = true\n[extension]\nkeep = 'yes'\nflag = true\ncount = 5\nitems = ['a']\n";
    let patch =
        toml::from_str("[daemon]\ntoken = ''\ntitle_min_chars = 0\nworker_templates = []\n[daemon.usage_items.claude]\npoll = false\n[extension]\nflag = false\ncount = 0\nitems = []\n")
            .unwrap();
    let prepared = document::patch(&old, text, patch).unwrap();
    let raw: toml::Value = toml::from_str(&prepared.text).unwrap();
    assert_eq!(prepared.candidate.daemon.token, "");
    assert_eq!(prepared.candidate.daemon.title_min_chars, 0);
    assert!(prepared.candidate.daemon.worker_templates.is_empty());
    assert!(!prepared.candidate.daemon.usage_items["claude"].poll);
    assert_eq!(raw["extension"]["keep"].as_str(), Some("yes"));
    assert_eq!(raw["extension"]["flag"].as_bool(), Some(false));
    assert_eq!(raw["extension"]["count"].as_integer(), Some(0));
    assert!(raw["extension"]["items"].as_array().unwrap().is_empty());
    assert_eq!(prepared.candidate.sessions.len(), 1);
}

#[test]
fn root_edits_reject_inline_sections_even_when_empty_and_malformed_settings() {
    let old = workspace();
    for key in ["project", "session", "host_terminal", "library"] {
        let text = format!("{key} = []");
        assert!(document::replace(&old, &text).is_err());
        assert!(document::patch(&old, "", toml::from_str(&text).unwrap()).is_err());
        assert!(document::patch(&old, &text, toml::from_str("").unwrap()).is_err());
    }
    assert!(document::replace(&old, "[daemon]\ntoken = false").is_err());
    assert!(document::patch(&old, "", toml::Value::Boolean(false)).is_err());
}
