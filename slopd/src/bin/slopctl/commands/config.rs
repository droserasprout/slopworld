//! Remote settings reads and single-leaf patches; the daemon owns validation and persistence.

use super::super::format::print_json;
use super::super::http::{Endpoint, request};
use super::Command;
use super::common::{arg, only};
use crate::shared::protocol::routes;
use crate::shared::wire;
use serde_json::{Value, json};

pub(crate) const CONFIG_USAGE: &str = "usage:
  slopctl config get KEY
  slopctl config set KEY VALUE

Read or persist a setting on the selected running daemon. Requires the root token.
KEY uses dotted paths; escape dots in map keys as ~1 and tildes as ~0.
String settings take literal text; booleans, numbers and lists take JSON.
Only individual values under daemon, defaults and commands can be set.
Token reads are redacted. Setting daemon.token invalidates the old token immediately;
saved remote peers must be reconnected with the new token.
Setting daemon.bind takes effect after restarting the daemon.
";

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum ConfigArgs {
    Get { key: String },
    Set { key: String, value: String },
}

pub(super) fn parse(args: &[String]) -> Result<Command, String> {
    let action = arg(args, 1, CONFIG_USAGE)?;
    let key = arg(args, 2, "config needs a setting key")?.to_owned();
    let command = match action {
        "get" => {
            only(args, 3)?;
            ConfigArgs::Get { key }
        }
        "set" => {
            let value = arg(args, 3, "config set needs a value")?.to_owned();
            only(args, 4)?;
            ConfigArgs::Set { key, value }
        }
        _ => return Err(CONFIG_USAGE.into()),
    };
    Ok(Command::Config(command))
}

fn parts(key: &str) -> Vec<String> {
    key.split('.')
        .map(|part| part.replace("~1", ".").replace("~0", "~"))
        .collect()
}

fn lookup<'a>(values: &'a Value, key: &str) -> Result<&'a Value, String> {
    parts(key)
        .iter()
        .try_fold(values, |value, part| value.get(part))
        .ok_or_else(|| format!("unknown config path: {key}"))
}

fn patch(values: &Value, key: &str, text: &str) -> Result<Value, String> {
    let parts = parts(key);
    if parts.len() < 2
        || !matches!(
            parts.first().map(String::as_str),
            Some("daemon" | "defaults" | "commands")
        )
    {
        return Err("Choose a setting under daemon, defaults or commands.".into());
    }
    let current = lookup(values, key).ok();
    if current.is_some_and(Value::is_object) {
        return Err("Choose a config path that identifies one value.".into());
    }
    let value = if current.is_some_and(Value::is_string) {
        Value::String(text.into())
    } else {
        // Optional fields and new map entries may be absent from the read view.
        // Their schema is checked below instead of requiring a preexisting value.
        let value: Value = match serde_json::from_str(text) {
            Ok(value) => value,
            Err(_) if current.is_none() => Value::String(text.into()),
            Err(_) => return Err("This setting needs a JSON boolean, number or list.".into()),
        };
        if value.is_null() || value.is_object() {
            return Err("A setting value cannot be null or an object.".into());
        }
        value
    };
    // Construct only the selected leaf, avoiding a read/replace race with other edits.
    let mut selected = value;
    for part in parts.iter().rev() {
        selected = json!({part: selected});
    }
    // Serde errors may quote input values, including credentials.
    serde_json::from_value::<wire::EditableConfig>(selected.clone())
        .map_err(|_error| format!("Unknown setting or incorrect value type: {key}"))?;
    Ok(json!({"values": selected, "paths": [key]}))
}

pub(super) fn run(
    endpoint: &Endpoint,
    session: &str,
    json_output: bool,
    args: ConfigArgs,
) -> Result<(), String> {
    let config = request(endpoint, session, "GET", routes::CONFIG, None)?;
    let values = config
        .get("values")
        .ok_or("Config response is missing values.")?;
    match args {
        ConfigArgs::Get { key } => {
            let value = lookup(values, &key)?;
            if !json_output && let Some(text) = value.as_str() {
                println!("{text}");
            } else {
                print_json(value);
            }
        }
        ConfigArgs::Set { key, value } => {
            let body = patch(values, &key, &value)?;
            request(endpoint, session, "PUT", routes::CONFIG_PATCH, Some(body))?;
            let restart_required = key == "daemon.bind";
            let token_changed = key == "daemon.token";
            if json_output {
                print_json(&json!({"saved": true, "restart_required": restart_required,
                    "token_changed": token_changed}));
            } else if restart_required {
                println!("Saved {key}; restart the daemon to apply the listener address.");
            } else if token_changed {
                println!(
                    "Saved {key}; the old token is invalid. Reconnect saved remote peers with the new token."
                );
            } else {
                println!("Saved {key}.");
            }
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "config_tests.rs"]
mod tests;
