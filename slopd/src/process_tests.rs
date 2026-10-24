use super::*;
use std::{fs, process::Command as StdCommand, time::Instant};

struct DescendantFixture {
    directory: std::path::PathBuf,
    pid: Option<i32>,
}

impl DescendantFixture {
    async fn spawn(leader_exits: bool) -> (Self, ChildGroup) {
        let directory =
            std::env::temp_dir().join(format!("slopd-process-group-{}", uuid::Uuid::new_v4()));
        fs::create_dir(&directory).unwrap();
        let mut command = Command::new("sh");
        // The child announces readiness before waiting. No timeout depends on
        // how quickly a loaded machine schedules the shell.
        let script = if leader_exits {
            "sh -c 'echo $$ > pid; while [ ! -f release ]; do sleep 0.01; done; echo late > wrote' & exit 0"
        } else {
            "sh -c 'echo $$ > pid; while [ ! -f release ]; do sleep 0.01; done; echo late > wrote'; :"
        };
        command.current_dir(&directory).args(["-c", script]);
        let child = spawn_child(&mut command, false).unwrap();
        let pid = tokio::time::timeout(Duration::from_secs(5), async {
            loop {
                if let Ok(pid) = fs::read_to_string(directory.join("pid"))
                    && let Ok(pid) = pid.trim().parse()
                {
                    break pid;
                }
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("descendant did not start");
        (
            Self {
                directory,
                pid: Some(pid),
            },
            child,
        )
    }

    async fn assert_terminated(&mut self) {
        let pid = self.pid.unwrap();
        tokio::time::timeout(Duration::from_secs(5), async {
            loop {
                // Orphans may briefly remain zombies until the host init reaps them.
                let status = fs::read_to_string(format!("/proc/{pid}/status"));
                if status
                    .as_ref()
                    .is_err_and(|error| error.kind() == io::ErrorKind::NotFound)
                    || status.as_ref().is_ok_and(|text| {
                        text.lines()
                            .any(|line| line.starts_with("State:") && line.contains("Z (zombie)"))
                    })
                {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("descendant survived command cleanup");
        self.pid = None;
        fs::write(self.directory.join("release"), "go").unwrap();
        assert!(!self.directory.join("wrote").exists());
    }
}

impl Drop for DescendantFixture {
    fn drop(&mut self) {
        // Release the harmless child even when an assertion fails.
        drop(fs::write(self.directory.join("release"), "go"));
        if let Some(pid) = self.pid {
            // SAFETY: this fixture owns the still-running child PID.
            unsafe {
                nix::libc::kill(pid, nix::libc::SIGKILL);
            }
        }
        drop(fs::remove_dir_all(&self.directory));
    }
}

#[tokio::test]
async fn timeout_terminates_descendants_even_after_the_leader_exits() {
    for leader_exits in [false, true] {
        let (mut fixture, child) = DescendantFixture::spawn(leader_exits).await;
        let error = capture_child(
            child,
            None,
            Duration::from_millis(100),
            CaptureLimits {
                stdout: 8,
                stderr: 8,
            },
        )
        .await
        .unwrap_err();
        assert!(error.downcast_ref::<TimedOut>().is_some());
        fixture.assert_terminated().await;
    }
}

#[tokio::test]
async fn cancellation_terminates_descendants() {
    let (mut fixture, child) = DescendantFixture::spawn(false).await;
    let mut capture = Box::pin(capture_child(
        child,
        None,
        Duration::from_secs(30),
        CaptureLimits {
            stdout: 8,
            stderr: 8,
        },
    ));
    assert!(futures::poll!(capture.as_mut()).is_pending());
    drop(capture);
    fixture.assert_terminated().await;
}

#[tokio::test]
async fn zero_limit_distinguishes_empty_from_discarded_output() {
    for (input, expected_truncated) in [(&b""[..], false), (&b"payload"[..], true)] {
        let (output, truncated) = read_bounded(input, 0).await.unwrap();
        assert!(output.is_empty());
        assert_eq!(truncated, expected_truncated);
    }
}

#[tokio::test]
async fn bounded_reader_drains_past_the_limit_and_propagates_late_errors() {
    use tokio::io::AsyncReadExt;
    struct FailedReader;
    impl AsyncRead for FailedReader {
        fn poll_read(
            self: std::pin::Pin<&mut Self>,
            _: &mut std::task::Context<'_>,
            _: &mut tokio::io::ReadBuf<'_>,
        ) -> std::task::Poll<io::Result<()>> {
            std::task::Poll::Ready(Err(io::Error::new(
                io::ErrorKind::ConnectionReset,
                "lost stream",
            )))
        }
    }
    let reader = (&b"more than the limit"[..]).chain(FailedReader);
    let error = read_bounded(reader, 4).await.unwrap_err();
    assert_eq!(error.kind(), io::ErrorKind::ConnectionReset);
}

#[tokio::test]
async fn missing_executable_preserves_spawn_error() {
    let missing = std::env::temp_dir().join(format!("slopd-missing-{}", uuid::Uuid::new_v4()));
    let error = run_bounded(
        &mut Command::new(missing),
        Duration::from_secs(5),
        CaptureLimits {
            stdout: 8,
            stderr: 8,
        },
    )
    .await
    .unwrap_err();
    assert_eq!(
        error.downcast_ref::<io::Error>().unwrap().kind(),
        io::ErrorKind::NotFound
    );
}

#[cfg(unix)]
#[tokio::test]
async fn nonzero_exit_preserves_both_streams_and_independent_limits() {
    let mut command = Command::new("sh");
    command.args(["-c", "printf abc; printf error >&2; exit 7"]);
    let output = run_bounded(
        &mut command,
        Duration::from_secs(5),
        CaptureLimits {
            stdout: 3,
            stderr: 2,
        },
    )
    .await
    .unwrap();
    assert_eq!(output.status.code(), Some(7));
    assert_eq!(output.stdout, b"abc");
    assert_eq!(output.stderr, b"er");
    assert!(!output.stdout_truncated);
    assert!(output.stderr_truncated);
}

#[cfg(unix)]
#[tokio::test]
async fn supplied_stdin_is_closed_after_writing() {
    let mut command = Command::new("sh");
    command.args(["-c", "cat; printf eof"]);
    let output = run_bounded_with_stdin(
        &mut command,
        b"payload",
        Duration::from_secs(5),
        CaptureLimits {
            stdout: 32,
            stderr: 32,
        },
    )
    .await
    .unwrap();
    assert!(output.status.success());
    assert_eq!(output.stdout, b"payloadeof");
    assert!(output.stderr.is_empty());
    assert!(!output.stdout_truncated);
    assert!(!output.stderr_truncated);
}

#[tokio::test]
async fn exact_limit_is_not_truncated() {
    let (output, truncated) = read_bounded(&b"hello"[..], 5).await.unwrap();
    assert_eq!(output, b"hello");
    assert!(!truncated);
}

#[tokio::test]
async fn limit_plus_one_is_truncated_and_retains_the_prefix() {
    let (output, truncated) = read_bounded(&b"hello!"[..], 5).await.unwrap();
    assert_eq!(output, b"hello");
    assert!(truncated);
}

#[cfg(unix)]
#[tokio::test]
async fn large_continuing_streams_are_drained_without_deadlock() {
    let mut command = Command::new("sh");
    command.arg("-c").arg(
        "dd if=/dev/zero bs=65536 count=128 2>/dev/null & \
             dd if=/dev/zero bs=65536 count=128 >&2 2>/dev/null & wait",
    );

    let output = run_bounded(
        &mut command,
        Duration::from_secs(5),
        CaptureLimits {
            stdout: 1024,
            stderr: 2048,
        },
    )
    .await
    .unwrap();

    assert!(output.status.success());
    assert_eq!(output.stdout.len(), 1024);
    assert_eq!(output.stderr.len(), 2048);
    assert!(output.stdout_truncated);
    assert!(output.stderr_truncated);
}

#[cfg(unix)]
#[tokio::test]
async fn supplied_stdin_and_large_outputs_are_drained_concurrently() {
    let mut command = Command::new("sh");
    command.arg("-c").arg(
        "dd if=/dev/zero bs=65536 count=128 2>/dev/null; \
             cat >/dev/null",
    );
    let input = vec![b'x'; 256 * 1024];

    let output = run_bounded_with_stdin(
        &mut command,
        &input,
        Duration::from_secs(5),
        CaptureLimits {
            stdout: 1024,
            stderr: 1024,
        },
    )
    .await
    .unwrap();

    assert!(output.status.success());
    assert_eq!(output.stdout.len(), 1024);
    assert!(output.stdout_truncated);
}

#[cfg(unix)]
#[tokio::test]
async fn timeout_kills_and_reaps_the_child() {
    let marker = std::env::temp_dir().join(format!("slopd-process-timeout-{}", std::process::id()));
    drop(fs::remove_file(&marker));
    let marker = marker.to_string_lossy().replace('\'', "'\\''");
    let mut command = Command::new("sh");
    command
        .arg("-c")
        .arg(format!("echo $$ > '{marker}'; while :; do :; done"));
    let child = spawn_child(&mut command, false).unwrap();
    tokio::time::timeout(Duration::from_secs(5), async {
        while !std::path::Path::new(&marker).exists() {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("child did not publish readiness");
    let pid = fs::read_to_string(&marker).unwrap();
    let started = Instant::now();

    let error = capture_child(
        child,
        None,
        Duration::from_millis(200),
        CaptureLimits {
            stdout: 1024,
            stderr: 1024,
        },
    )
    .await
    .unwrap_err();

    assert!(error.downcast_ref::<TimedOut>().is_some());
    assert!(started.elapsed() < Duration::from_secs(2));

    let alive = StdCommand::new("kill")
        .args(["-0", pid.trim()])
        .stderr(std::process::Stdio::null())
        .status()
        .unwrap()
        .success();
    drop(fs::remove_file(&marker));
    assert!(!alive, "timed-out child was not killed and reaped");
}

#[cfg(unix)]
#[tokio::test]
async fn stdin_failure_terminates_and_reaps_without_waiting_for_timeout() {
    let marker = std::env::temp_dir().join(format!("slopd-stdin-error-{}", uuid::Uuid::new_v4()));
    let script = format!(
        "echo $$ > '{}'; exec 0<&-; while :; do :; done",
        marker.display()
    );
    let mut command = Command::new("sh");
    command.args(["-c", &script]);
    let started = Instant::now();
    let error = run_bounded_with_stdin(
        &mut command,
        &vec![b'x'; 1024 * 1024],
        Duration::from_secs(10),
        CaptureLimits {
            stdout: 8,
            stderr: 8,
        },
    )
    .await
    .unwrap_err();
    assert!(error.downcast_ref::<TimedOut>().is_none(), "{error:#}");
    assert!(started.elapsed() < Duration::from_secs(5));
    let pid = fs::read_to_string(&marker).unwrap();
    assert!(
        !StdCommand::new("kill")
            .args(["-0", pid.trim()])
            .stderr(Stdio::null())
            .status()
            .unwrap()
            .success()
    );
    fs::remove_file(marker).unwrap();
}
