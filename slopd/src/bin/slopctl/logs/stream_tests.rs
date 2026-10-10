use super::*;
use std::process::{Command, Stdio};

#[test]
fn a_live_child_with_closed_stdout_remains_cancelable_while_reaping() {
    let mut command = Command::new("sh");
    command
        .args(["-c", "exec 1>&-; exec sleep 30"])
        .stdout(Stdio::piped());
    let mut child = command.spawn().unwrap();
    // EOF proves the child has closed stdout without exiting.
    let mut reader = BufReader::new(child.stdout.take().unwrap());
    assert_eq!(reader.read_line(&mut String::new()).unwrap(), 0);
    let child = Arc::new(Mutex::new(child));
    assert!(child.lock().unwrap().try_wait().unwrap().is_none());
    let waiter = Arc::clone(&child);
    let (done, receiver) = mpsc::channel();
    let (waiting, waiting_receiver) = mpsc::sync_channel(1);
    let worker = thread::spawn(move || {
        let mut waiting = Some(waiting);
        drop(done.send(wait_child_observed(&waiter, LogSource::Game, || {
            if let Some(waiting) = waiting.take() {
                waiting.send(()).unwrap();
            }
        })));
    });
    waiting_receiver
        .recv_timeout(Duration::from_secs(2))
        .expect("waiter must begin reaping before cancellation");
    let cancel = Arc::clone(&child);
    let (killed, kill_receiver) = mpsc::channel();
    let killer = thread::spawn(move || {
        kill_child(&cancel);
        killed.send(()).unwrap();
    });
    kill_receiver
        .recv_timeout(Duration::from_secs(2))
        .expect("cancellation must not wait for child exit");
    assert!(
        receiver
            .recv_timeout(Duration::from_secs(2))
            .unwrap()
            .is_err()
    );
    killer.join().unwrap();
    worker.join().unwrap();
    assert!(child.lock().unwrap().try_wait().unwrap().is_some());
}

#[test]
fn a_full_fan_in_queue_releases_its_worker_when_receiver_drops() {
    // Zero capacity forces the first line to wait for the consumer.
    let (sender, receiver) = mpsc::sync_channel(0);
    let mut command = Command::new("sh");
    command
        .args(["-c", "printf 'first\nsecond\n'; exec sleep 30"])
        .stdout(Stdio::piped());
    let (child, worker) = spawn_command(LogSource::Game, command, sender, false).unwrap();
    assert!(
        matches!(receiver.recv_timeout(Duration::from_secs(2)).unwrap(), LogEvent::Line(LogSource::Game, line) if line == "first")
    );
    drop(receiver);
    let (done, done_receiver) = mpsc::channel();
    let joiner = thread::spawn(move || {
        worker.join().unwrap();
        done.send(()).unwrap();
    });
    done_receiver
        .recv_timeout(Duration::from_secs(2))
        .expect("receiver drop must cancel and reap the producer");
    joiner.join().unwrap();
    assert!(child.lock().unwrap().try_wait().unwrap().is_some());
}

#[test]
fn container_stdout_and_stderr_share_structured_output_and_completion() {
    let (sender, receiver) = mpsc::sync_channel(FAN_IN_CAPACITY);
    let mut command = Command::new("sh");
    command.args(["-c", "printf 'normal\\n'; printf 'crash\\n' >&2"]);
    let (child, worker) = spawn_command(LogSource::Daemon, command, sender, true).unwrap();
    let mut output = Vec::new();
    for expected in ["normal", "crash"] {
        let LogEvent::Line(source, line) = receiver.recv_timeout(Duration::from_secs(2)).unwrap()
        else {
            panic!("both streams must be read before completion");
        };
        assert_eq!(source, LogSource::Daemon);
        assert_eq!(line, expected);
        write_log_line(&mut output, source, &line, true, true).unwrap();
    }
    assert!(matches!(
        receiver.recv_timeout(Duration::from_secs(2)).unwrap(),
        LogEvent::Finished(LogSource::Daemon, Ok(()))
    ));
    worker.join().unwrap();
    assert!(child.lock().unwrap().try_wait().unwrap().is_some());
    let lines = String::from_utf8(output).unwrap();
    let values = lines
        .lines()
        .map(|line| serde_json::from_str::<serde_json::Value>(line).unwrap())
        .collect::<Vec<_>>();
    assert_eq!(
        values,
        [
            serde_json::json!({"source": "daemon", "line": "normal"}),
            serde_json::json!({"source": "daemon", "line": "crash"})
        ]
    );
}

#[test]
fn captured_stderr_is_cancelled_when_a_full_queue_loses_its_receiver() {
    let (sender, receiver) = mpsc::sync_channel(0);
    let mut command = Command::new("sh");
    command.args(["-c", "printf 'first\\nsecond\\n' >&2; exec sleep 30"]);
    let (child, worker) = spawn_command(LogSource::Daemon, command, sender, true).unwrap();
    assert!(
        matches!(receiver.recv_timeout(Duration::from_secs(2)).unwrap(), LogEvent::Line(LogSource::Daemon, line) if line == "first")
    );
    drop(receiver);
    let (done, done_receiver) = mpsc::channel();
    let joiner = thread::spawn(move || {
        worker.join().unwrap();
        done.send(()).unwrap();
    });
    done_receiver
        .recv_timeout(Duration::from_secs(2))
        .expect("stderr producer must be cancelled and reaped");
    joiner.join().unwrap();
    assert!(child.lock().unwrap().try_wait().unwrap().is_some());
}
