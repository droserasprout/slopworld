#[path = "src/version.rs"]
mod version;

use std::env;
use std::process::Command;

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=src/version.rs");
    println!("cargo:rerun-if-changed=../.git/HEAD");
    println!("cargo:rerun-if-changed=../.git/index");
    println!("cargo:rerun-if-changed=../.git/packed-refs");
    println!("cargo:rerun-if-changed=../.git/refs/tags");
    println!("cargo:rerun-if-env-changed=SLOPWORLD_BUILD_VERSION");

    let fallback = env!("CARGO_PKG_VERSION");
    let version = env::var("SLOPWORLD_BUILD_VERSION")
        .ok()
        .filter(|version| !version.trim().is_empty())
        .unwrap_or_else(|| version_from_git(fallback));

    println!("cargo:rustc-env=SLOPWORLD_VERSION={version}");
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
