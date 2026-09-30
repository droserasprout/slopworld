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
    assert!(receiver
        .recv_timeout(Duration::from_secs(2))
        .unwrap()
        .is_err());
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
    let (child, worker) = spawn_command(LogSource::Game, command, sender).unwrap();
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
