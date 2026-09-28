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

    #[derive(Serialize)]
    struct FixtureRef<'a> {
        tasks: &'a [Task],
    }
    let config = scratch.0.join("config.toml");
    for count in [10, 100, 1_000] {
        // Seed in one write, outside measurement, so setup is not itself quadratic.
        let fixture = Fixture {
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
        let _ = std::fs::remove_file(config.with_file_name("tasks.journal"));
        let snapshot = std::fs::read(config.with_file_name("tasks.toml"))?;
        let mut tasks = Tasks::load(&config)?;
        assert_eq!(tasks.all().len(), count);
        measure(&format!("task list clone {count} records"), |_| {
            std::hint::black_box(tasks.all()).len()
        });
        measure(&format!("task visible 10 percent {count} records"), |_| {
            std::hint::black_box(tasks.visible("worker")).len()
        });
        measure_prepared(
            &format!("task create journal {count} records"),
            || {
                std::fs::write(config.with_file_name("tasks.toml"), &snapshot).unwrap();
                let _ = std::fs::remove_file(config.with_file_name("tasks.journal"));
                Tasks::load(&config).unwrap()
            },
            |tasks| {
                tasks
                    .create_owned(
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
                    )
                    .unwrap();
            },
        );
        measure_prepared(
            &format!("task create snapshot reference {count} records"),
            || fixture.tasks.clone(),
            |tasks| {
                let mut task = tasks[0].clone();
                task.id = "new-task".into();
                tasks.push(task);
                crate::paths::write_private_toml(
                    &config.with_file_name("reference.toml"),
                    &toml::to_string_pretty(&FixtureRef { tasks }).unwrap(),
                )
                .unwrap();
            },
        );
        // Restore the fixture after the independent creation probes.
        std::fs::write(config.with_file_name("tasks.toml"), &snapshot)?;
        let _ = std::fs::remove_file(config.with_file_name("tasks.journal"));
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
    let initial = Tasks::load(&config)?;
    std::fs::write(
        config.with_file_name("tasks.toml"),
        toml::to_string(&Fixture {
            tasks: initial.all(),
        })?,
    )?;
    let _ = std::fs::remove_file(config.with_file_name("tasks.journal"));
    let mut tasks = Tasks::load(&config)?;
    for sample in 0..10_000 {
        tasks.update(
            "worker",
            "bench-0",
            Status::Working,
            Some(format!("restart {sample}")),
        )?;
    }
    measure("task restart bounded 10000 updates", |_| {
        Tasks::load(&config).unwrap().all().len()
    });
    Ok(())
}

// Fixed single-operation samples allow seeding outside the timer without growing the catalog
// throughout calibration. These creation percentiles are per operation, unlike batch probes.
fn measure_prepared<T>(name: &str, mut prepare: impl FnMut() -> T, mut action: impl FnMut(&mut T)) {
    let mut timings = Vec::new();
    for sample in 0..60 {
        let mut value = prepare();
        let start = std::time::Instant::now();
        action(&mut value);
        if sample >= 10 {
            timings.push(start.elapsed().as_secs_f64() * 1e6);
        }
    }
    timings.sort_by(f64::total_cmp);
    println!("{name:<44} p50={:.3} p95={:.3}", timings[24], timings[46]);
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
                    .unwrap();
                cache.flush().unwrap();
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
                        .unwrap();
                }
                cache.flush().unwrap();
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
