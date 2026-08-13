use std::process::Stdio;
use std::time::Duration;

use anyhow::{bail, Result};
use tokio::process::Command;

const TIMEOUT: Duration = Duration::from_secs(3);

const HANDOFF: Duration = Duration::from_millis(700);

const OPENERS: &[&str] = &["xdg-open", "gio", "wslview"];

const SCHEMES: &[&str] = &["http://", "https://", "mailto:"];

pub async fn url(template: &str, u: &str) -> Result<()> {
    let u = u.trim();
    check(u)?;

    let template = template.trim();
    if !template.is_empty() {
        let mut argv = crate::sandbox::shell_split(template);
        if argv.is_empty() {
            bail!("URL opener is empty after splitting");
        }
        let mut used = false;
        for arg in &mut argv {
            if arg.contains("{url}") {
                *arg = arg.replace("{url}", u);
                used = true;
            }
        }
        if !used {
            argv.push(u.to_string());
        }
        return match tokio::time::timeout(TIMEOUT, one(&argv)).await {
            Err(_) => bail!("configured URL opener timed out"),
            Ok(Ok(())) => Ok(()),
            Ok(Err(e)) if missing(&e) => fallback_url(u).await,
            Ok(Err(e)) => Err(e),
        };
    }

    fallback_url(u).await
}

async fn fallback_url(u: &str) -> Result<()> {
    let mut last: Option<anyhow::Error> = None;
    for tool in OPENERS {
        let argv: Vec<String> = if *tool == "gio" {
            vec!["gio".into(), "open".into(), u.into()]
        } else {
            vec![(*tool).into(), u.into()]
        };
        match tokio::time::timeout(TIMEOUT, one(&argv)).await {
            Err(_) => last = Some(anyhow::anyhow!("{tool} timed out")),
            Ok(Ok(())) => return Ok(()),
            Ok(Err(e)) => {
                if !missing(&e) {
                    last = Some(e);
                }
            }
        }
    }
    match last {
        Some(e) => Err(e),
        None => bail!("no opener on this host (xdg-open, gio or wslview)"),
    }
}

pub fn check(u: &str) -> Result<()> {
    if u.is_empty() {
        bail!("empty url");
    }
    if u.len() > 2048 {
        bail!("url too long");
    }
    if u.chars().any(|c| c.is_control() || c.is_whitespace()) {
        bail!("url has whitespace or control characters in it");
    }
    let lower = u.to_ascii_lowercase();
    if !SCHEMES.iter().any(|s| lower.starts_with(s)) {
        bail!("refusing to open {u}: only http, https and mailto");
    }
    Ok(())
}

fn missing(e: &anyhow::Error) -> bool {
    e.downcast_ref::<std::io::Error>()
        .map(|io| io.kind() == std::io::ErrorKind::NotFound)
        .unwrap_or(false)
}

async fn one(argv: &[String]) -> Result<()> {
    let mut child = Command::new(&argv[0])
        .args(&argv[1..])
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()?;

    match tokio::time::timeout(HANDOFF, child.wait()).await {
        Ok(Ok(status)) if status.success() => Ok(()),
        Ok(Ok(status)) => bail!("{} {}", argv[0], status),
        Ok(Err(e)) => Err(e.into()),
        Err(_) => Ok(()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn takes_web_schemes_only() {
        assert!(check("https://example.com/a?b=c#d").is_ok());
        assert!(check("HTTP://EXAMPLE.COM").is_ok());
        assert!(check("mailto:someone@example.com").is_ok());

        assert!(check("file:///etc/shadow").is_err());
        assert!(check("javascript:alert(1)").is_err());
        assert!(check("ms-settings:").is_err());
        assert!(check("/tmp/x").is_err());
        assert!(check("").is_err());
        assert!(check("https://a b").is_err());
        assert!(check("https://a\nrm -rf").is_err());
        assert!(check(&format!("https://{}", "x".repeat(4000))).is_err());
    }

    #[test]
    fn opener_template_expands_or_appends_url() {
        let mut argv = crate::sandbox::shell_split("xdg-open {url}");
        assert_eq!(argv, vec!["xdg-open", "{url}"]);
        for arg in &mut argv {
            *arg = arg.replace("{url}", "https://example.com/a");
        }
        assert_eq!(argv, vec!["xdg-open", "https://example.com/a"]);

        let mut append = crate::sandbox::shell_split("gio open");
        append.push("https://example.com/a".into());
        assert_eq!(append, vec!["gio", "open", "https://example.com/a"]);
    }
}
