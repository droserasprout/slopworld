use super::*;

#[test]
fn ages_saturate_and_switch_units_at_the_boundary() {
    let now = 200_000_000;
    for (elapsed, expected) in [
        (0, "0s ago"),
        (59_999, "59s ago"),
        (60_000, "1m ago"),
        (3_599_999, "59m ago"),
        (3_600_000, "1h ago"),
        (86_399_999, "23h ago"),
        (86_400_000, "1d ago"),
    ] {
        assert_eq!(age_at(now - elapsed, now), expected);
    }
    assert_eq!(age_at(now + 1000, now), "0s ago");
}

#[test]
fn human_and_json_output_preserve_task_and_status_details() {
    const CHILD: &str = "SLOPCTL_TEST_FORMAT_OUTPUT";
    if std::env::var_os(CHILD).is_none() {
        let output = std::process::Command::new(std::env::current_exe().unwrap())
            .args([
                "--exact",
                "format::tests::human_and_json_output_preserve_task_and_status_details",
                "--nocapture",
            ])
            .env(CHILD, "1")
            .output()
            .unwrap();
        assert!(output.status.success());
        let stdout = String::from_utf8(output.stdout).unwrap();
        assert!(stdout.contains(concat!(
                "BEGIN OUTPUT\n",
                "t1  alice -> bob  [pending]  0s ago, moved 0s ago\n",
                "  do work\n  progress\n  worker child under alice (durable)\n",
                "?  ? -> ?  [?]  0s ago\n  \n  worker ? under ? (one-shot)\n",
                "?  ? -> ?  [?]  0s ago\n  \nworker   named\n",
                "?  ? -> ?  [?]  0s ago\n  \nworker   fallback\n",
                "?  ? -> ?  [?]  0s ago\n  \nworker   ?\n",
                "{\n  \"ok\": true\n}\n",
                "{\n  \"tasks\": []\n}\n",
                "session   agent\nendpoint  local\ndaemon    unreachable: offline\n",
                "session   ?\nendpoint  ?\ndaemon    unreachable: ?\n",
                "session   agent\nendpoint  local\ndaemon    ok, slopd 1.2.3\npending   2 for you, 3 you sent\n",
                "session   ?\nendpoint  ?\ndaemon    ok, slopd ?\npending   unknown: denied\n",
                "session   ?\nendpoint  ?\ndaemon    ok, slopd ?\npending   unknown: ?\n",
                "END OUTPUT\n"
            )), "{stdout}");
        return;
    }
    // Future timestamps clamp to zero, keeping output deterministic without a clock mock.
    let task = json!({"created_ms": u64::MAX});
    println!("BEGIN OUTPUT");
    emit(
        &json!({"tasks": [
            {"id":"t1", "from":"alice", "to":"bob", "status":"pending", "body":"do work",
             "created_ms": u64::MAX - 1, "updated_ms":u64::MAX, "note":"progress",
             "worker":{"session":"child", "parent":"alice", "durable":true}},
            {"created_ms":u64::MAX, "worker":{}}
        ]}),
        false,
    );
    for worker in [
        json!({"name":"named", "session":"ignored"}),
        json!({"session":"fallback"}),
        json!({}),
    ] {
        emit(&json!({"task":task, "worker":worker}), false);
    }
    emit(&json!({"tasks":[]}), false);
    emit(&json!({"ok":true}), false);
    emit(&json!({"tasks":[]}), true);
    for status in [
        json!({"session":"agent", "endpoint":"local", "error":"offline"}),
        json!({}),
        json!({"session":"agent", "endpoint":"local", "daemon":"ok", "version":"1.2.3", "waiting":2, "sent":3}),
        json!({"daemon":"ok", "waiting":2, "error":"denied"}),
        json!({"daemon":"ok"}),
    ] {
        print_status(&status);
    }
    println!("END OUTPUT");
}
