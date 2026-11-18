//! Command-line entry point; application lifecycle lives in launcher.
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    slopweb::launcher::run().await
}
