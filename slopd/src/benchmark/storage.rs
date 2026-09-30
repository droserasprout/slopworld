//! Storage scaling probes. Fixtures and writes stay in a disposable directory. No daemon,
//! tmux server, or user configuration is opened. Filesystem timings include atomic replacement
//! on the temporary filesystem, not fsync or a cold-disk guarantee.
#![expect(
    clippy::expect_used,
    reason = "benchmark fixture validation aborts measurements on unexpected storage or serialization failure"
)]

use std::path::{Path, PathBuf};

use anyhow::Result;
use serde::Serialize;

use super::measure;
use crate::session::State;
use crate::tasks::{Status, Task, Tasks};

#[derive(Serialize)]
struct TaskFixture {
    tasks: Vec<Task>,
}

#[derive(Serialize)]
struct TaskFixtureRef<'a> {
    tasks: &'a [Task],
}

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
    let config = scratch.0.join("config.toml");
    for count in [10, 100, 1_000] {
        benchmark_task_size(&config, count)?;
    }
    benchmark_task_restart(&config)
}

fn benchmark_task_size(config: &Path, count: usize) -> Result<()> {
    // Seed in one write, outside measurement, so setup is not itself quadratic.
    let fixture = TaskFixture {
        tasks: (0..count)
            .map(|index| Task {
                from_id: crate::tasks::HOST.into(),
                to_id: if index % 10 == 0 { "worker" } else { "other" }.into(),
                id: format!("bench-{index:x}"),
                from: crate::tasks::HOST.into(),
                to: if index % 10 == 0 { "worker" } else { "other" }.into(),
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
    drop(std::fs::remove_file(config.with_file_name("tasks.journal")));
    let snapshot = std::fs::read(config.with_file_name("tasks.toml"))?;
    let mut tasks = Tasks::load(config)?;
    anyhow::ensure!(
        tasks.all().len() == count,
        "seeded {} tasks, expected {count}",
        tasks.all().len()
    );
    measure(&format!("task list clone {count} records"), |_| {
        std::hint::black_box(tasks.all()).len()
    });
    measure(&format!("task visible 10 percent {count} records"), |_| {
        std::hint::black_box(tasks.visible("worker")).len()
    });
    measure_prepared(
        &format!("task create journal {count} records"),
        || {
            std::fs::write(config.with_file_name("tasks.toml"), &snapshot)?;
            drop(std::fs::remove_file(config.with_file_name("tasks.journal")));
            Tasks::load(config)
        },
        |tasks| {
            tasks.create_owned(
                crate::tasks::Participant {
                    name: "host".into(),
                    identity: "host".into(),
                },
                crate::tasks::Participant {
                    name: "worker".into(),
                    identity: "worker".into(),
                },
                "x".repeat(1024),
                None,
            )?;
            Ok(())
        },
    )?;
    measure_prepared(
        &format!("task create snapshot reference {count} records"),
        || Ok(fixture.tasks.clone()),
        |tasks| {
            let mut task = tasks
                .first()
                .cloned()
                .ok_or_else(|| anyhow::anyhow!("task fixture is empty"))?;
            task.id = "new-task".into();
            tasks.push(task);
            crate::paths::write_private_toml(
                &config.with_file_name("reference.toml"),
                &toml::to_string_pretty(&TaskFixtureRef { tasks })?,
            )?;
            Ok(())
        },
    )?;
    // Restore the fixture after the independent creation probes.
    std::fs::write(config.with_file_name("tasks.toml"), &snapshot)?;
    drop(std::fs::remove_file(config.with_file_name("tasks.journal")));
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
    Ok(())
}

fn benchmark_task_restart(config: &Path) -> Result<()> {
    let initial = Tasks::load(config)?;
    std::fs::write(
        config.with_file_name("tasks.toml"),
        toml::to_string(&TaskFixture {
            tasks: initial.all(),
        })?,
    )?;
    drop(std::fs::remove_file(config.with_file_name("tasks.journal")));
    let mut tasks = Tasks::load(config)?;
    for sample in 0..10_000 {
        tasks.update(
            "worker",
            "bench-0",
            Status::Working,
            Some(format!("restart {sample}")),
        )?;
    }
    let expected_count = tasks.all().len();
    let restarted = Tasks::load(config)?;
    anyhow::ensure!(
        restarted.all().len() == expected_count,
        "restart lost task records"
    );
    anyhow::ensure!(
        restarted
            .get("worker", "bench-0")
            .and_then(|task| task.note)
            == Some("restart 9999".into()),
        "restart lost the final task update"
    );
    measure("task restart 10000 updates", |_| {
        Tasks::load(config)
            .expect("persisted benchmark task fixture loads")
            .all()
            .len()
    });
    Ok(())
}

// Fixed single-operation samples allow seeding outside the timer without growing the catalog
// throughout calibration. These creation percentiles are per operation, unlike batch probes.
fn measure_prepared<T>(
    name: &str,
    mut prepare: impl FnMut() -> Result<T>,
    mut action: impl FnMut(&mut T) -> Result<()>,
) -> Result<()> {
    let mut timings = Vec::new();
    for sample in 0..60 {
        let mut value = prepare()?;
        let start = std::time::Instant::now();
        action(&mut value)?;
        if sample >= 10 {
            timings.push(start.elapsed().as_secs_f64() * 1e6);
        }
    }
    timings.sort_by(f64::total_cmp);
    println!(
        "{name:<44} p50={:.3} p95={:.3}",
        timings.get(24).copied().expect("50 measured samples"),
        timings.get(46).copied().expect("50 measured samples")
    );
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
