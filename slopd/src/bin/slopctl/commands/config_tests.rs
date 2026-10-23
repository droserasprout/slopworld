use super::*;
use crate::commands::{command_help, parse_command};
use crate::tests::http_fixture::{serve, serve_sequence};

fn words(text: &str) -> Vec<String> {
    text.split_whitespace().map(str::to_owned).collect()
}

#[test]
fn config_commands_require_exact_arguments_and_support_help() {
    assert_eq!(
        parse_command(&words("config get daemon.bind")),
        Ok(Command::Config(ConfigArgs::Get {
            key: "daemon.bind".into()
        }))
    );
    assert_eq!(
        parse_command(&words("config set daemon.bind 192.168.1.10:7717")),
        Ok(Command::Config(ConfigArgs::Set {
            key: "daemon.bind".into(),
            value: "192.168.1.10:7717".into()
        }))
    );
    for args in [
        "config",
        "config get",
        "config set daemon.bind",
        "config get daemon.bind extra",
        "config set daemon.bind addr extra",
        "config edit daemon.bind",
    ] {
        assert!(parse_command(&words(args)).is_err(), "{args}");
    }
    assert_eq!(command_help("config"), Some(CONFIG_USAGE));
    for args in ["config --help", "config get --help", "config set --help"] {
        assert_eq!(
            parse_command(&words(args)),
            Ok(Command::Help {
                usage: CONFIG_USAGE
            })
        );
    }
}

#[test]
fn patches_select_only_one_leaf_and_preserve_literal_strings_and_empty_values() {
    let values = json!({"daemon": {"token": "<redacted>", "bind": "local", "worker_templates": ["old"],
        "usage_items": {"a.b~c": {"poll": true, "interval_secs": 12}}}});
    for (key, text, expected) in [
        ("daemon.token", "false", json!("false")),
        (
            "daemon.bind",
            "192.168.1.10:7717",
            json!("192.168.1.10:7717"),
        ),
        ("daemon.worker_templates", "[]", json!([])),
        ("daemon.usage_items.a~1b~0c.poll", "false", json!(false)),
        ("daemon.usage_items.a~1b~0c.interval_secs", "0", json!(0)),
        ("daemon.usage_items.new.poll", "false", json!(false)),
        ("daemon.usage_items.new.interval_secs", "60", json!(60)),
    ] {
        let body = patch(&values, key, text).unwrap();
        assert_eq!(body["paths"], json!([key]));
        assert_eq!(lookup(&body["values"], key).unwrap(), &expected);
        if key != "daemon.token" {
            assert!(body["values"]["daemon"].get("token").is_none());
        }
    }
    for (key, text) in [
        ("daemon.typo", "x"),
        ("daemon", "{}"),
        ("metadata.defaults", "x"),
        ("daemon.usage_items", "{}"),
        ("daemon.worker_templates", "oops"),
        ("daemon.worker_templates", "null"),
    ] {
        assert!(patch(&values, key, text).is_err(), "{key}");
    }
}

#[test]
fn config_set_uses_authenticated_patch_and_propagates_daemon_rejections() {
    for (status, reply, success) in [
        ("200 OK", json!({"ok": true}), true),
        (
            "400 Bad Request",
            json!({"error": "invalid listener"}),
            false,
        ),
    ] {
        let (endpoint, server) = serve_sequence(vec![
            (
                "200 OK",
                json!({"values": {"daemon": {"bind": "127.0.0.1:7717"}}}).to_string(),
            ),
            (status, reply.to_string()),
        ]);
        let result = run(
            &endpoint,
            "host",
            true,
            ConfigArgs::Set {
                key: "daemon.bind".into(),
                value: "192.168.1.10:7717".into(),
            },
        );
        if success {
            result.unwrap();
        } else {
            assert_eq!(result.unwrap_err(), "invalid listener");
        }
        let requests = server.join().unwrap().unwrap();
        assert!(requests.contains("GET /api/config "));
        assert!(requests.contains("PUT /api/config/patch "));
        assert!(requests.contains("192.168.1.10:7717"));
        assert!(requests.to_lowercase().contains("x-slop-token: secret"));
        assert!(requests.contains(r#""paths":["daemon.bind"]"#));
    }
}

#[test]
fn config_get_preserves_token_redaction() {
    let (endpoint, server) = serve(
        "200 OK",
        &json!({"values": {"daemon": {"token": "<redacted>"}}}).to_string(),
    );
    run(
        &endpoint,
        "host",
        true,
        ConfigArgs::Get {
            key: "daemon.token".into(),
        },
    )
    .unwrap();
    assert!(server.join().unwrap().unwrap().contains("GET /api/config "));
}
