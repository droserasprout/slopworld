//! Project live sessions into client views and publish session snapshots.

use super::super::*;

impl Manager {
    /// Build session views in name order, resolving launch policy and display metadata.
    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let worktrees = self.worktree_view_index().await;
        let live = self.live.read().await;
        let temp = self.temp.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| {
                let p = cfg.project_of(&l.cfg).or_else(|| temp.get(&l.cfg.project));
                // Temporary projects supply host-shell directories without creating sidebar groups.
                let display_project = if l.host && temp.contains_key(&l.cfg.project) {
                    String::new()
                } else {
                    l.cfg.project.clone()
                };
                SessionView {
                    worktree: l.cfg.worktree.clone(),
                    worktree_name: worktrees
                        .get(&l.cfg.worktree)
                        .map(|w| w.name.clone())
                        .unwrap_or_default(),
                    name: l.cfg.name.clone(),
                    label: l.cfg.label.clone().unwrap_or_default(),
                    intent: l.cfg.intent.clone(),
                    reader: SessionReaderView {
                        path: l.cfg.reader_path.clone(),
                        key: l.cfg.reader_key.clone(),
                        scope: l.cfg.reader_scope.clone(),
                        pinned: l.cfg.reader_pinned,
                        line: l.cfg.reader_line,
                    },
                    project: display_project,
                    dir: if l.host && !l.host_path.trim().is_empty() {
                        l.host_path.clone()
                    } else {
                        worktrees
                            .get(&l.cfg.worktree)
                            .filter(|w| p.is_some_and(|p| !p.id.is_empty() && w.project_id == p.id))
                            .map(|w| w.path.clone())
                            .unwrap_or_else(|| p.map(|p| p.dir.clone()).unwrap_or_default())
                    },
                    launch: SessionLaunchView {
                        command: l.cfg.command.clone(),
                        command_preset: cfg.command_name(&l.cfg),
                        cmd: l.cfg.cmd.clone(),
                        args: l.cfg.args.clone(),
                        sandbox: l.cfg.sandbox.clone(),
                        persistent_tmp: l.cfg.persistent_tmp,
                        agent: cfg.command_of(&l.cfg),
                        network: p.map(|p| cfg.network_of(&l.cfg, p)).unwrap_or_default(),
                        dns: p.map(|p| cfg.dns_of(&l.cfg, p)).unwrap_or_default(),
                        limits: p.map(|p| cfg.limits_of(&l.cfg, p)).unwrap_or(l.cfg.limits),
                        mounts: p.map(|p| p.mounts.clone()).unwrap_or_default(),
                        // Tasks own worker lifetimes; older configs may still carry parent launch flags.
                        autostart: l.cfg.autostart && !l.cfg.worker,
                        auto_resume: l.cfg.auto_resume && !l.cfg.worker,
                    },
                    worker: SessionWorkerView {
                        enabled: l.cfg.worker,
                        parent: l.cfg.parent.clone(),
                        task_id: l.cfg.task_id.clone(),
                        durable: l.cfg.worker && !l.ephemeral,
                    },
                    ephemeral: l.ephemeral,
                    host: l.host,
                    runtime: SessionRuntimeView {
                        auto_resume_pending: l.input.auto_resume_pending,
                        state: l.state,
                        alive: l.state != State::Down,
                        cols: l.cols,
                        rows: l.rows,
                        process_running: l.process_running,
                        last_change: l.last_change,
                        state_since: l.state_since,
                        title: l
                            .cfg
                            .label
                            .clone()
                            .filter(|label| !label.trim().is_empty())
                            .or_else(|| l.title.title().map(str::to_owned))
                            .or_else(|| l.screen.as_ref().map(|s| s.title.clone()))
                            .unwrap_or_default(),
                        bell: l.bell,
                        run_id: l.run_id,
                        seq: l.seq,
                    },
                }
            })
            .collect();
        out.sort_by(|a, b| a.name.cmp(&b.name));
        out
    }

    pub(super) async fn announce_sessions(&self) {
        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
    }
}
