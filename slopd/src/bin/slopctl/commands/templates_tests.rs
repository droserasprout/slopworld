use super::*;
use crate::tests::http_fixture::serve;

#[test]
fn catalog_displays_descriptions_names_and_empty_catalogs() {
    assert_eq!(
        render_templates(&json!({"templates": [
            {"name": "coder", "description": "Writes code"},
            {"name": "reviewer"}, {"description": "Unnamed"}
        ]})),
        "coder  -  Writes code\nreviewer\n?  -  Unnamed\n"
    );
    for value in [
        json!({}),
        json!({"templates": null}),
        json!({"templates": []}),
    ] {
        assert_eq!(render_templates(&value), "no templates\n");
    }
}

#[test]
fn details_display_launch_defaults_and_filter_non_string_sandbox_entries() {
    assert_eq!(
        render_template_details(&json!({
            "name": "coder", "description": "Writes code", "version": 2,
            "defaults": {"command": {"name": "codex"}, "cmd": "codex resume",
                "sandbox": ["base", 42, "rust"], "network": "host", "autostart": true}
        })),
        "coder  -  Writes code\nversion  2\ncommand  codex\ncmd      codex resume\nsandbox  base, rust\nnetwork  host\nautostart true\n"
    );
    assert_eq!(
        render_template_details(&json!({})),
        "?\nversion  0\nnetwork  private\nautostart false\n"
    );
    assert_eq!(
        render_template_details(&json!({"name": "bare", "defaults": {
            "command": {"name": ""}, "cmd": "", "sandbox": []
        }})),
        "bare\nversion  0\nsandbox  (none)\nnetwork  private\nautostart false\n"
    );
}

#[test]
fn human_commands_fetch_the_visible_catalog_and_select_details() {
    for (session, route) in [
        (HOST, "/api/templates"),
        ("agent", "/api/templates/spawnable"),
    ] {
        for show in [false, true] {
            let (endpoint, server) =
                serve("200 OK", r#"{"templates":[{"name":"coder","version":1}]}"#);
            let result = if show {
                run_template_show(&endpoint, session, false, "coder", None)
            } else {
                run_templates(&endpoint, session, false, None)
            };
            let request = server.join().unwrap().unwrap();
            result.unwrap();
            assert!(
                request.starts_with(&format!("GET {route} HTTP/1.1\r\n")),
                "{request}"
            );
            assert!(
                request
                    .to_lowercase()
                    .contains(&format!("x-slop-session: {session}\r\n"))
            );
        }
    }
}

#[test]
fn catalog_refusals_and_missing_details_are_reported_without_fallback() {
    for show in [false, true] {
        let (endpoint, server) = serve("403 Forbidden", r#"{"error":"outside grant"}"#);
        let result = if show {
            run_template_show(&endpoint, "agent", false, "coder", Some("repo"))
        } else {
            run_templates(&endpoint, "agent", false, Some("repo"))
        };
        server.join().unwrap().unwrap();
        assert_eq!(result.unwrap_err(), "outside grant");
    }
    let (endpoint, server) = serve("200 OK", r#"{"templates":[{"name":"other"}]}"#);
    let result = run_template_show(&endpoint, "agent", false, "coder", None);
    server.join().unwrap().unwrap();
    assert_eq!(result.unwrap_err(), "no accessible agent template: coder");
}
