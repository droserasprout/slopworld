//! Ordered terminal input batching.

use super::*;

pub(crate) enum Input {
    Traced(Box<Input>, Vec<Arc<crate::latency::InputTrace>>),
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
        let (item, mut traces) = match item {
            Input::Traced(item, traces) => (*item, traces),
            item => (item, Vec::new()),
        };
        let previous = out.last_mut().map(|last| match last {
            Input::Traced(inner, _) => inner.as_mut(),
            other => other,
        });
        let merged = match (previous, &item) {
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
        if merged && !traces.is_empty() {
            let previous = out.pop().unwrap();
            let (previous, mut prior) = match previous {
                Input::Traced(inner, prior) => (*inner, prior),
                item => (item, Vec::new()),
            };
            prior.append(&mut traces);
            out.push(Input::Traced(Box::new(previous), prior));
        } else if !merged {
            out.push(if traces.is_empty() {
                item
            } else {
                Input::Traced(Box::new(item), traces)
            });
        }
    }
    out
}
