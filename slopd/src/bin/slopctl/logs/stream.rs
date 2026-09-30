//! Own log readers and child lifetimes; selection and rendering stay in `logs`.
//! Drop the bounded receiver before cancellation and joins to release blocked senders.

use super::{LogSource, LogsOptions, clean_log_line, source_command, write_log_line};
use std::io::{self, BufRead, BufReader, LineWriter, Write};
use std::sync::{Arc, Mutex, mpsc};
use std::thread;
use std::time::Duration;

const FAN_IN_CAPACITY: usize = 256;

pub(super) fn run_one_log(
    source: LogSource,
    options: LogsOptions,
    json: bool,
) -> Result<(), String> {
    run_sources(&[source], options, json)
}

pub(super) fn run_all_logs(options: LogsOptions, json: bool) -> Result<(), String> {
    run_sources(&[LogSource::Game, LogSource::Daemon], options, json)
}

enum LogEvent {
    Line(LogSource, String),
    Finished(LogSource, Result<(), String>),
}

type SharedChild = Arc<Mutex<std::process::Child>>;

fn spawn_log_source(
    source: LogSource,
    options: LogsOptions,
    sender: mpsc::SyncSender<LogEvent>,
) -> Result<(SharedChild, thread::JoinHandle<()>), String> {
    spawn_command(source, source_command(source, options)?, sender)
}

fn spawn_command(
    source: LogSource,
    mut command: std::process::Command,
    sender: mpsc::SyncSender<LogEvent>,
) -> Result<(SharedChild, thread::JoinHandle<()>), String> {
    let mut child = command
        .spawn()
        .map_err(|e| format!("starting {} logs: {e}", source.name()))?;
    let Some(stdout) = child.stdout.take() else {
        drop(child.kill());
        drop(child.wait());
        return Err(format!("{} logs produced no output pipe", source.name()));
    };
    let child = Arc::new(Mutex::new(child));
    let worker_child = Arc::clone(&child);
    let worker = thread::spawn(move || {
        let mut reader = BufReader::new(stdout);
        let mut line = String::new();
        loop {
            line.clear();
            match reader.read_line(&mut line) {
                Ok(0) => break,
                Ok(_) => {
                    let line = clean_log_line(std::mem::take(&mut line));
                    if sender.send(LogEvent::Line(source, line)).is_err() {
                        kill_child(&worker_child);
                        drop(wait_child(&worker_child, source));
                        return;
                    }
                }
                Err(e) => {
                    kill_child(&worker_child);
                    drop(wait_child(&worker_child, source));
                    drop(sender.send(LogEvent::Finished(
                        source,
                        Err(format!("reading {} logs: {e}", source.name())),
                    )));
                    return;
                }
            }
        }

        let result = wait_child(&worker_child, source);
        drop(sender.send(LogEvent::Finished(source, result)));
    });
    Ok((child, worker))
}

fn kill_child(child: &SharedChild) {
    if let Ok(mut child) = child.lock() {
        drop(child.kill());
    }
}

fn wait_child(child: &SharedChild, source: LogSource) -> Result<(), String> {
    wait_child_observed(child, source, || {})
}

fn wait_child_observed(
    child: &SharedChild,
    source: LogSource,
    mut waiting: impl FnMut(),
) -> Result<(), String> {
    let status = loop {
        // Never hold the child mutex during a blocking wait. Cleanup retains
        // access to kill even after the reader sees EOF from a live source.
        let status = child
            .lock()
            .map_err(|_error| format!("waiting for {} logs: child lock poisoned", source.name()))?
            .try_wait()
            .map_err(|e| format!("waiting for {} logs: {e}", source.name()))?;
        if let Some(status) = status {
            break status;
        }
        waiting();
        thread::sleep(Duration::from_millis(20));
    };
    if status.success() {
        Ok(())
    } else {
        Err(format!(
            "{} log command exited with {status}",
            source.name()
        ))
    }
}

fn run_sources(sources: &[LogSource], options: LogsOptions, json: bool) -> Result<(), String> {
    let (sender, receiver) = mpsc::sync_channel(FAN_IN_CAPACITY);
    let mut children = Vec::new();
    let mut workers = Vec::new();
    let mut errors = Vec::new();
    for &source in sources {
        match spawn_log_source(source, options, sender.clone()) {
            Ok((child, worker)) => {
                children.push(child);
                workers.push(worker);
            }
            Err(error) => errors.push(format!("{}: {error}", source.name())),
        }
    }
    drop(sender);

    let stdout = io::stdout();
    let mut output = LineWriter::new(stdout.lock());
    let mut output_error = None;

    for event in &receiver {
        match event {
            LogEvent::Line(source, line) => {
                if let Err(e) = write_log_line(&mut output, source, &line, sources.len() > 1, json)
                {
                    output_error = Some(e);
                    break;
                }
            }
            LogEvent::Finished(source, result) => {
                if let Err(error) = result {
                    errors.push(format!("{}: {error}", source.name()));
                }
            }
        }
    }

    // Release senders waiting on a full queue before killing or joining.
    drop(receiver);
    // Also kill child processes after an output error.
    // A worker can still be waiting for a process that follows logs but has no new output.
    if output_error.is_some() {
        for child in &children {
            kill_child(child);
        }
    }
    for worker in workers {
        drop(worker.join());
    }

    if let Some(error) = output_error {
        if error.kind() == io::ErrorKind::BrokenPipe {
            return Ok(());
        }
        return Err(format!("writing logs: {error}"));
    }
    output.flush().map_err(|e| format!("writing logs: {e}"))?;
    if errors.is_empty() {
        Ok(())
    } else {
        Err(errors.join(". "))
    }
}

#[cfg(test)]
#[path = "stream_tests.rs"]
mod tests;
