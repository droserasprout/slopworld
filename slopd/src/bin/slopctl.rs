use std::io::Read;
use std::path::PathBuf;
use std::process::ExitCode;

use serde::Deserialize;
use serde_json::{json, Value};

const USAGE: &str = "slopctl - delegate work between SlopWorld agents

usage:
  slopctl delegate AGENT TASK...
  slopctl inbox
  slopctl task ID
  slopctl accept ID [NOTE...]
  slopctl progress ID [NOTE...]
  slopctl finish ID [RESULT...]
  slopctl fail ID [ERROR...]

SLOPWORLD_SESSION identifies the caller. SLOPD_ENDPOINT selects endpoint.json.
";

#[derive(Deserialize)]
struct Endpoint {
    url: String,
    token: String,
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("slopctl: {e}");
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<(), String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.is_empty() || matches!(args[0].as_str(), "-h" | "--help" | "help") {
        print!("{USAGE}");
        return Ok(());
    }
    let endpoint = load_endpoint()?;
    let session = std::env::var("SLOPWORLD_SESSION")
        .map_err(|_| "SLOPWORLD_SESSION is not set".to_string())?;
    let value = match args[0].as_str() {
        "delegate" if args.len() >= 3 => request(
            &endpoint,
            &session,
            "POST",
            "/api/tasks",
            Some(json!({"to": args[1], "body": args[2..].join(" ")})),
        )?,
        "inbox" if args.len() == 1 => request(&endpoint, &session, "GET", "/api/tasks", None)?,
        "task" if args.len() == 2 => request(
            &endpoint,
            &session,
            "GET",
            &format!("/api/tasks/{}", args[1]),
            None,
        )?,
        command @ ("accept" | "progress" | "finish" | "fail") if args.len() >= 2 => {
            let status = match command {
                "accept" => "accepted",
                "progress" => "working",
                "finish" => "done",
                _ => "failed",
            };
            let note = (args.len() > 2).then(|| args[2..].join(" "));
            request(
                &endpoint,
                &session,
                "POST",
                &format!("/api/tasks/{}", args[1]),
                Some(json!({"status": status, "note": note})),
            )?
        }
        _ => return Err(format!("invalid arguments\n\n{USAGE}")),
    };
    print_value(&value);
    Ok(())
}

fn load_endpoint() -> Result<Endpoint, String> {
    if let Ok(url) = std::env::var("SLOPD_URL") {
        return Ok(Endpoint {
            url,
            token: std::env::var("SLOPD_TOKEN").unwrap_or_default(),
        });
    }
    let path = std::env::var("SLOPD_ENDPOINT")
        .map(PathBuf::from)
        .unwrap_or_else(|_| {
            dirs::config_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("slopworld/endpoint.json")
        });
    let text =
        std::fs::read_to_string(&path).map_err(|e| format!("reading {}: {e}", path.display()))?;
    serde_json::from_str(&text).map_err(|e| format!("parsing {}: {e}", path.display()))
}

fn request(
    endpoint: &Endpoint,
    session: &str,
    method: &str,
    path: &str,
    body: Option<Value>,
) -> Result<Value, String> {
    let url = format!("{}{}", endpoint.url.trim_end_matches('/'), path);
    let mut res = match (method, body) {
        ("GET", None) => ureq::get(&url)
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .call(),
        ("POST", Some(value)) => ureq::post(&url)
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .send_json(value),
        _ => return Err(format!("unsupported request: {method}")),
    }
    .map_err(|e| format!("request: {e}"))?;
    let status = res.status();
    let mut text = String::new();
    res.body_mut()
        .as_reader()
        .read_to_string(&mut text)
        .map_err(|e| format!("response: {e}"))?;
    let value: Value =
        serde_json::from_str(&text).map_err(|e| format!("response {status}: {e}"))?;
    if !status.is_success() {
        return Err(value
            .get("error")
            .and_then(Value::as_str)
            .unwrap_or(&text)
            .to_string());
    }
    Ok(value)
}

fn print_value(v: &Value) {
    if let Some(tasks) = v.get("tasks").and_then(Value::as_array) {
        for t in tasks {
            print_task(t);
        }
    } else if let Some(task) = v.get("task") {
        print_task(task);
    } else {
        println!("{}", serde_json::to_string_pretty(v).unwrap());
    }
}

fn print_task(t: &Value) {
    println!(
        "{}  {} -> {}  [{}]\n  {}",
        t["id"].as_str().unwrap_or("?"),
        t["from"].as_str().unwrap_or("?"),
        t["to"].as_str().unwrap_or("?"),
        t["status"].as_str().unwrap_or("?"),
        t["body"].as_str().unwrap_or("")
    );
    if let Some(note) = t["note"].as_str() {
        println!("  {note}");
    }
}
