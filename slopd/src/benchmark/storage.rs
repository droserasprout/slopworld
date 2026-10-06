//! Storage scaling probes. Fixtures and writes stay in a disposable directory. No daemon,
//! tmux server, or user configuration is opened. Filesystem timings include atomic replacement
//! on the temporary filesystem, not fsync or a cold-disk guarantee.
#![expect(
    clippy::expect_used,
    reason = "benchmark fixture validation aborts measurements on unexpected storage or serialization failure"
)]

use std::path::PathBuf;

use anyhow::Result;

use super::measure;
use crate::session::State;
use crate::tasks::{Status, Task, Tasks};

struct Scratch(PathBuf);

impl Drop for Scratch {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.0));
    }
}

pub(super) fn run() -> Result<()> {
    let scratch = Scratch(
        std::env::temp_dir().join(format!("slopworld-storage-bench-{}", uuid::Uuid::new_v4())),
    );
    std::fs::create_dir(&scratch.0)?;
    println!("storage fixtures: temporary filesystem, warm cache, task bodies=1024 bytes");
    benchmark_tasks(&scratch)?;
    benchmark_activity(&scratch)?;
    benchmark_worktree_parse()?;
    Ok(())
}

fn benchmark_tasks(scratch: &Scratch) -> Result<()> {
    for count in [10, 100, 1_000, 10_000] {
        let data = scratch.0.join(format!("tasks-{count}"));
        std::fs::create_dir_all(data.join("tasks"))?;
        for index in 0..count {
            let task = Task {
                id: format!("{index:016x}"),
                from: "host".into(),
                to: "worker".into(),
                from_id: "host".into(),
                to_id: "worker".into(),
                body: "x".repeat(1024),
                status: Status::Working,
                note: None,
                summary: None,
                created_ms: 1,
                updated_ms: 1,
                worker: None,
            };
            let mut raw = toml::Value::try_from(&task)?;
            raw.as_table_mut()
                .expect("task table")
                .insert("storage_order".into(), toml::Value::Integer(index));
            crate::paths::write_private_toml(
                &data.join("tasks").join(format!("{}.toml", task.id)),
                &toml::to_string(&raw)?,
            )?;
        }
        measure(&format!("task startup {count} records"), |_| {
            Tasks::load_records(&data)
                .expect("task records load")
                .all()
                .len()
        });
        let mut tasks = Tasks::load_records(&data)?;
        measure(&format!("task record update {count} records"), |sample| {
            tasks
                .update(
                    "worker",
                    "0000000000000000",
                    Status::Working,
                    Some(format!("progress {sample}")),
                )
                .expect("task update")
                .body
                .len()
        });
    }
    Ok(())
}

fn benchmark_activity(scratch: &Scratch) -> Result<()> {
    for count in [1, 32, 128] {
        let path = scratch.0.join(format!("activity-{count}.toml"));
        let cache = crate::activity::ActivityCache::load(path);
        for index in 0..count {
            cache.remember(&format!("agent-{index}"), State::Working, 1)?;
        }
        measure(
            &format!("activity remember+queue {count} records"),
            |sample| {
                cache
                    .remember("agent-0", State::Working, sample as u64 + 2)
                    .expect("benchmark activity update");
                sample
            },
        );
        cache.flush()?;
        measure(
            &format!("activity remember+flush {count} records"),
            |sample| {
                cache
                    .remember("agent-0", State::Working, sample as u64 + 1_000_000)
                    .expect("benchmark activity update");
                cache.flush().expect("benchmark activity flush");
                sample
            },
        );
        measure(
            &format!("activity burst32+flush {count} records"),
            |sample| {
                for update in 0..32 {
                    cache
                        .remember(
                            "agent-0",
                            State::Working,
                            (sample * 32 + update) as u64 + 2_000_000,
                        )
                        .expect("benchmark activity update");
                }
                cache.flush().expect("benchmark activity flush");
                sample
            },
        );
    }
    Ok(())
}

fn benchmark_worktree_parse() -> Result<()> {
    for count in [10, 100, 1_000] {
        let store = crate::worktrees::Store {
            worktrees: (0..count)
                .map(|index| crate::worktrees::Worktree {
                    id: format!("worktree-{index}"),
                    project_id: "project".into(),
                    name: format!("feature-{index}"),
                    path: format!("/bench/worktrees/{index}/checkout"),
                    repository: "/bench/repo".into(),
                    managed: true,
                    initial_branch: format!("feature-{index}"),
                    base: "main".into(),
                    phase: "ready".into(),
                    error: String::new(),
                })
                .collect(),
        };
        let text = toml::to_string(&store)?;
        // Isolate the synchronous parser used by Store::load. File reads, view construction,
        // and per-session worktree lookups are additional costs, excluded here.
        measure(&format!("worktree TOML parse {count} records"), |_| {
            toml::from_str::<crate::worktrees::Store>(std::hint::black_box(&text))
                .expect("benchmark worktree parse")
                .worktrees
                .len()
        });
    }
    Ok(())
}
