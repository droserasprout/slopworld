use serde_json::{Value, json};
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
        "{}  {} -> {}  [{}]  {}",
        t["id"].as_str().unwrap_or("?"),
        t["from"].as_str().unwrap_or("?"),
        t["to"].as_str().unwrap_or("?"),
        t["status"].as_str().unwrap_or("?"),
        when
    );
    print_task_text(t["body"].as_str().unwrap_or(""));
    if let Some(note) = t["note"].as_str() {
        print_task_text(note);
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

/// Frame continuation and empty lines so task text cannot resemble a new record.
fn print_task_text(text: &str) {
    println!("  {}", text.replace('\n', "\n  "));
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

/// Format the elapsed time since a timestamp for the inbox.
/// Use the largest whole unit available: days, hours, minutes, or seconds.
pub(crate) fn age(ms: u64) -> String {
    let now = u64::try_from(
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_millis(),
    )
    .unwrap_or(u64::MAX);
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
#[path = "format_tests.rs"]
mod tests;
