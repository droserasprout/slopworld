//! Storage scaling probes. Fixtures and writes stay in a disposable directory. No daemon,
//! tmux server, or user configuration is opened. Filesystem timings include atomic replacement
//! on the temporary filesystem, not fsync or a cold-disk guarantee.

use std::path::PathBuf;

use anyhow::Result;
use serde::Serialize;

use super::measure;
use crate::session::State;
use crate::tasks::{Status, Task, Tasks};

struct Scratch(PathBuf);

impl Drop for Scratch {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
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
    #[derive(Serialize)]
    struct Fixture {
        tasks: Vec<Task>,
    }

    let config = scratch.0.join("config.toml");
    for count in [10, 100, 1_000] {
        // Seed in one write, outside measurement, so setup is not itself quadratic.
        let fixture = Fixture {
            tasks: (0..count)
                .map(|index| Task {
                    id: format!("bench-{index:x}"),
                    from: crate::tasks::HOST.into(),
                    to: "worker".into(),
                    body: "x".repeat(1_024),
                    status: Status::Working,
                    note: None,
                    summary: None,
                    created_ms: 1,
                    updated_ms: 1,
                    worker: None,
                })
                .collect(),
        };
        std::fs::write(
            config.with_file_name("tasks.toml"),
            toml::to_string_pretty(&fixture)?,
        )?;
        let mut tasks = Tasks::load(&config)?;
        assert_eq!(tasks.all().len(), count);
        measure(&format!("task list clone {count} records"), |_| {
            std::hint::black_box(tasks.all()).len()
        });
        measure(&format!("task update+save {count} records"), |sample| {
            // Update the first record: history serialization dominates without a long lookup.
            tasks
                .update(
                    "worker",
                    "bench-0",
                    Status::Working,
                    Some(format!("progress {sample}")),
                )
                .expect("benchmark task update")
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
            &format!("activity remember+save {count} records"),
            |sample| {
                cache
                    .remember("agent-0", State::Working, sample as u64 + 2)
                    .expect("benchmark activity update");
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
