//! Ordered terminal input batching.

use super::*;

// Let the terminal application process typed input before the next Enter.
pub(super) const ENTER_GAP: Duration = Duration::from_millis(150);

pub(crate) enum Input {
    Keys { keys: Vec<String>, literal: bool },
    Bytes(Vec<u8>),
    // Keep the paste intact. tmux adds paste markers only when the receiving application requests them.
    Paste { bytes: Vec<u8> },
    // Executed by the single consumer, preserving the pause relative to queued input.
    Gap(Duration),
}

/// A flat command and the traces that belong to the terminal send it represents.
pub(crate) struct QueuedInput {
    pub(crate) command: Input,
    pub(crate) traces: Vec<Arc<crate::latency::InputTrace>>,
}

impl QueuedInput {
    pub(crate) fn new(command: Input) -> Self {
        Self {
            command,
            traces: Vec::new(),
        }
    }

    #[cfg(test)]
    pub(crate) fn traced(command: Input, trace: Arc<crate::latency::InputTrace>) -> Self {
        Self {
            command,
            traces: vec![trace],
        }
    }
}

impl From<Input> for QueuedInput {
    fn from(command: Input) -> Self {
        Self::new(command)
    }
}

// Leave headroom: tmux 3.7c send-keys -H delivered 996 bytes but rejected 997.
pub(super) const INPUT_BATCH: usize = 800;
const INPUT_KEYS: usize = 100;

pub(super) fn merge_input(items: Vec<QueuedInput>) -> Vec<QueuedInput> {
    let mut out: Vec<QueuedInput> = Vec::new();
    for mut item in items {
        let merged = if let Some(previous) = out.last_mut() {
            let merged = match (&mut previous.command, &item.command) {
                (Input::Bytes(acc), Input::Bytes(b)) if acc.len() + b.len() <= INPUT_BATCH => {
                    acc.extend_from_slice(b);
                    true
                }
                (
                    Input::Keys {
                        keys: acc,
                        literal: had,
                    },
                    Input::Keys { keys, literal },
                ) if *had == *literal && acc.len() + keys.len() <= INPUT_KEYS => {
                    acc.extend(keys.iter().cloned());
                    true
                }
                _ => false,
            };
            if merged {
                previous.traces.append(&mut item.traces);
            }
            merged
        } else {
            false
        };
        if !merged {
            out.push(item);
        }
    }
    out
}
