use std::io;
use std::process::{ExitStatus, Stdio};
use std::time::Duration;

use anyhow::{Context, Result};
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWriteExt};
use tokio::process::Command;

pub(crate) struct CaptureLimits {
    pub(crate) stdout: usize,
    pub(crate) stderr: usize,
}

#[derive(Debug)]
pub(crate) struct BoundedOutput {
    pub(crate) status: ExitStatus,
    pub(crate) stdout: Vec<u8>,
    pub(crate) stderr: Vec<u8>,
    pub(crate) stdout_truncated: bool,
    pub(crate) stderr_truncated: bool,
}

#[derive(Debug)]
pub(crate) struct TimedOut;

impl std::fmt::Display for TimedOut {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("process timed out")
    }
}

impl std::error::Error for TimedOut {}

/// Spawn a child with isolated standard streams, drain both bounded outputs, and reap it.
/// Callers decide what its exit status and truncation mean to their operation.
pub(crate) async fn run_bounded(
    command: &mut Command,
    timeout: Duration,
    limits: CaptureLimits,
) -> Result<BoundedOutput> {
    run_bounded_with_input(command, None, timeout, limits).await
}

/// Spawn a child with supplied stdin, drain both bounded outputs and reap it. Input is written
/// concurrently with output draining so a producer that fills stdout cannot deadlock the
/// caller while it is still consuming stdin.
pub(crate) async fn run_bounded_with_stdin(
    command: &mut Command,
    input: &[u8],
    timeout: Duration,
    limits: CaptureLimits,
) -> Result<BoundedOutput> {
    run_bounded_with_input(command, Some(input), timeout, limits).await
}

async fn run_bounded_with_input(
    command: &mut Command,
    input: Option<&[u8]>,
    timeout: Duration,
    limits: CaptureLimits,
) -> Result<BoundedOutput> {
    let mut child = command
        .kill_on_drop(true)
        .stdin(if input.is_some() {
            Stdio::piped()
        } else {
            Stdio::null()
        })
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;
    let stdout = child.stdout.take().context("capturing stdout")?;
    let stderr = child.stderr.take().context("capturing stderr")?;
    let stdin = child.stdin.take();
    if input.is_some() && stdin.is_none() {
        let _ = child.kill().await;
        let _ = child.wait().await;
        anyhow::bail!("capturing stdin");
    }

    let write_stdin = async move {
        match (stdin, input) {
            (Some(mut stdin), Some(input)) => {
                stdin.write_all(input).await.map_err(anyhow::Error::from)
            }
            (Some(_), None) | (None, None) => Ok(()),
            (None, Some(_)) => anyhow::bail!("capturing stdin"),
        }
    };

    let collected = tokio::time::timeout(timeout, async {
        let stdout = read_bounded(stdout, limits.stdout);
        let stderr = read_bounded(stderr, limits.stderr);
        let status = child.wait();
        let (stdout, stderr, status, input) = tokio::join!(stdout, stderr, status, write_stdin);
        let (stdout, stdout_truncated) = stdout?;
        let (stderr, stderr_truncated) = stderr?;
        input?;
        Ok::<_, anyhow::Error>(BoundedOutput {
            status: status?,
            stdout,
            stderr,
            stdout_truncated,
            stderr_truncated,
        })
    })
    .await;

    match collected {
        Ok(result) => result,
        Err(_) => {
            let _ = child.kill().await;
            let _ = child.wait().await;
            Err(anyhow::Error::new(TimedOut))
        }
    }
}

/// Read to EOF while retaining only the configured prefix. Continuing to drain is important:
/// stopping at the prefix can fill the child's pipe and deadlock it before `wait` completes.
pub(crate) async fn read_bounded<R: AsyncRead + Unpin>(
    mut reader: R,
    limit: usize,
) -> io::Result<(Vec<u8>, bool)> {
    let mut output = Vec::with_capacity(limit.min(4096));
    let mut truncated = false;
    let mut buffer = [0u8; 4096];
    loop {
        let read = reader.read(&mut buffer).await?;
        if read == 0 {
            break;
        }
        let room = limit.saturating_sub(output.len());
        let take = room.min(read);
        output.extend_from_slice(&buffer[..take]);
        if take < read {
            truncated = true;
        }
    }
    Ok((output, truncated))
}

#[cfg(test)]
#[path = "process_tests.rs"]
mod tests;
