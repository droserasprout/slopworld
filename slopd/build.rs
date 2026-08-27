use std::env;
use std::path::Path;
use std::process::Command;

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=../tools/version.sh");
    println!("cargo:rerun-if-changed=../.git/HEAD");
    println!("cargo:rerun-if-changed=../.git/index");
    println!("cargo:rerun-if-changed=../.git/packed-refs");
    println!("cargo:rerun-if-changed=../.git/refs/tags");
    println!("cargo:rerun-if-env-changed=SLOPWORLD_BUILD_VERSION");

    let fallback = env!("CARGO_PKG_VERSION");
    let version = env::var("SLOPWORLD_BUILD_VERSION")
        .ok()
        .filter(|version| !version.trim().is_empty())
        .unwrap_or_else(|| {
            let script = Path::new(env!("CARGO_MANIFEST_DIR")).join("../tools/version.sh");
            Command::new("sh")
                .arg(script)
                .arg(fallback)
                .output()
                .ok()
                .filter(|output| output.status.success())
                .map(|output| String::from_utf8_lossy(&output.stdout).trim().to_owned())
                .filter(|version| !version.is_empty())
                .unwrap_or_else(|| fallback.to_owned())
        });

    println!("cargo:rustc-env=SLOPWORLD_VERSION={version}");
}
