#[path = "src/version.rs"]
mod version;

use std::env;
use std::process::Command;

fn main() -> std::io::Result<()> {
    protobuf()?;
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=src/version.rs");
    watch_git();
    println!("cargo:rerun-if-env-changed=SLOPWORLD_BUILD_VERSION");

    let fallback = env!("CARGO_PKG_VERSION");
    let version = env::var("SLOPWORLD_BUILD_VERSION")
        .ok()
        .filter(|version| !version.trim().is_empty())
        .unwrap_or_else(|| version_from_git(fallback));

    println!("cargo:rustc-env=SLOPWORLD_VERSION={version}");
    Ok(())
}

// rev-parse resolves both ordinary repositories and linked-worktree metadata.
// Version resolution uses commits and tags, not the index. Watching the index
// would rebuild the binaries after staging or a Git status refresh.
fn watch_git() {
    let repo = env!("CARGO_MANIFEST_DIR").to_string() + "/..";
    let mut paths = vec!["HEAD".to_string(), "packed-refs".into(), "refs/tags".into()];
    if let Some(active_ref) = git(&repo, &["symbolic-ref", "-q", "HEAD"]) {
        paths.push(active_ref);
    }
    for path in paths {
        if let Some(resolved) = git(
            &repo,
            &["rev-parse", "--path-format=absolute", "--git-path", &path],
        ) {
            println!("cargo:rerun-if-changed={resolved}");
        }
    }
}

fn version_from_git(fallback: &str) -> String {
    let repo = env!("CARGO_MANIFEST_DIR").to_string() + "/..";
    let tag = git(&repo, &["describe", "--tags", "--exact-match", "HEAD"]);
    let hash = git(&repo, &["rev-parse", "--short", "HEAD"]);
    let date = Command::new("date")
        .args(["-u", "+%Y%m%d"])
        .output()
        .ok()
        .filter(|output| output.status.success())
        .map(|output| String::from_utf8_lossy(&output.stdout).trim().to_owned())
        .filter(|date| !date.is_empty())
        .unwrap_or_else(|| "00000000".to_string());

    version::resolve(fallback, tag.as_deref(), hash.as_deref(), &date)
}

fn git(repo: &str, args: &[&str]) -> Option<String> {
    Command::new("git")
        .args(["-C", repo])
        .args(args)
        .output()
        .ok()
        .filter(|output| output.status.success())
        .map(|output| String::from_utf8_lossy(&output.stdout).trim().to_owned())
        .filter(|value| !value.is_empty())
}

fn protobuf() -> std::io::Result<()> {
    println!("cargo:rerun-if-changed=../shared/slopworld.proto");
    let mut config = prost_build::Config::new();
    config.message_attribute(
        ".",
        "#[derive(serde::Serialize, serde::Deserialize)] #[serde(default, deny_unknown_fields)]",
    );
    config.boxed(".slopworld.PresetRequest.definition.sandbox");
    config.enum_attribute(".", "#[derive(serde::Serialize, serde::Deserialize)]");
    let files = config.load_fds(&["../shared/slopworld.proto"], &["../shared"])?;
    for file in &files.file {
        for message in &file.message_type {
            for field in &message.field {
                if field.proto3_optional.unwrap_or(false)
                    || (field.r#type == Some(11) && field.label != Some(3))
                {
                    config.field_attribute(
                        format!(".slopworld.{}.{}", message.name(), field.name()),
                        r#"#[serde(skip_serializing_if="Option::is_none")]"#,
                    );
                }
            }
        }
    }
    for name in [
        ".slopworld.Project.expanded_dir",
        ".slopworld.SandboxPreset.source",
        ".slopworld.CommandPreset.source",
    ] {
        config.field_attribute(name, "#[serde(skip_serializing)]");
    }
    config.compile_fds(files)
}
