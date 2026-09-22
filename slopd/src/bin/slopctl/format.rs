use serde_json::{json, Value};
use std::time::{SystemTime, UNIX_EPOCH};

pub(crate) fn emit(v: &Value, json: bool) {
    if json {
        print_json(v);
    } else {
        print_value(v);
    }
}

pub(crate) fn print_json(v: &Value) {
    println!("{}", serde_json::to_string_pretty(v).unwrap_or_default());
}

pub(crate) fn print_value(v: &Value) {
    if let Some(tasks) = v.get("tasks").and_then(Value::as_array) {
        for t in tasks {
            print_task(t);
        }
    } else if let Some(task) = v.get("task") {
        print_task(task);
        if let Some(worker) = v.get("worker") {
            let name = worker["name"]
                .as_str()
                .or_else(|| worker["session"].as_str())
                .unwrap_or("?");
            println!("worker   {name}");
        }
    } else {
        print_json(v);
    }
}

pub(crate) fn print_task(t: &Value) {
    let created = t["created_ms"].as_u64().unwrap_or(0);
    let updated = t["updated_ms"].as_u64().unwrap_or(created);
    let mut when = age(created);
    if updated > created {
        when.push_str(&format!(", moved {}", age(updated)));
    }
    println!(
        "{}  {} -> {}  [{}]  {}\n  {}",
        t["id"].as_str().unwrap_or("?"),
        t["from"].as_str().unwrap_or("?"),
        t["to"].as_str().unwrap_or("?"),
        t["status"].as_str().unwrap_or("?"),
        when,
        t["body"].as_str().unwrap_or("")
    );
    if let Some(note) = t["note"].as_str() {
        println!("  {note}");
    }
    if let Some(worker) = t.get("worker") {
        println!(
            "  worker {} under {}{}",
            worker["session"].as_str().unwrap_or("?"),
            worker["parent"].as_str().unwrap_or("?"),
            if worker["durable"].as_bool().unwrap_or(false) {
                " (durable)"
            } else {
                " (one-shot)"
            }
        );
    }
}

pub(crate) fn print_status(v: &Value) {
    println!("session   {}", v["session"].as_str().unwrap_or("?"));
    println!("endpoint  {}", v["endpoint"].as_str().unwrap_or("?"));
    if v["daemon"] != json!("ok") {
        println!(
            "daemon    unreachable: {}",
            v["error"].as_str().unwrap_or("?")
        );
        return;
    }
    println!(
        "daemon    ok, slopd {}",
        v["version"].as_str().unwrap_or("?")
    );
    match (v["waiting"].as_u64(), v["sent"].as_u64()) {
        (Some(waiting), Some(sent)) => println!("pending   {waiting} for you, {sent} you sent"),
        _ => println!("pending   unknown: {}", v["error"].as_str().unwrap_or("?")),
    }
}

/// How long ago, in the coarsest unit that still says something. An inbox is read to find what
/// has been sitting, so hours and days are the answer; the exact second never is.
pub(crate) fn age(ms: u64) -> String {
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64;
    age_at(ms, now)
}

fn age_at(ms: u64, now: u64) -> String {
    let secs = now.saturating_sub(ms) / 1000;
    match secs {
        0..=59 => format!("{secs}s ago"),
        60..=3599 => format!("{}m ago", secs / 60),
        3600..=86399 => format!("{}h ago", secs / 3600),
        _ => format!("{}d ago", secs / 86400),
    }
}

#[cfg(test)]
mod tests {
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
}
