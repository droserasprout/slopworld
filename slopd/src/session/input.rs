//! Ordered terminal input batching.

use super::*;

pub(crate) enum Input {
    Keys { keys: Vec<String>, literal: bool },
    Bytes(Vec<u8>),
    // Keep the paste intact. tmux adds paste markers only when the receiving application requests them.
    Paste { bytes: Vec<u8> },
    // Executed by the single consumer, preserving the pause relative to queued input.
    Gap(Duration),
}

// tmux -H uses one argument for each byte. Its command message has a limit of approximately 1 KiB.
pub(super) const INPUT_BATCH: usize = 800;
const INPUT_KEYS: usize = 100;

pub(super) fn merge_input(items: Vec<Input>) -> Vec<Input> {
    let mut out: Vec<Input> = Vec::new();
    for item in items {
        let merged = match (out.last_mut(), &item) {
            (Some(Input::Bytes(acc)), Input::Bytes(b)) if acc.len() + b.len() <= INPUT_BATCH => {
                acc.extend_from_slice(b);
                true
            }
            (
                Some(Input::Keys {
                    keys: acc,
                    literal: had,
                }),
                Input::Keys { keys, literal },
            ) if *had == *literal && acc.len() + keys.len() <= INPUT_KEYS => {
                acc.extend(keys.iter().cloned());
                true
            }
            _ => false,
        };
        if !merged {
            out.push(item);
        }
    }
    out
}
