//! Session-to-project lookup scaling, including per-snapshot index construction.

use std::hint::black_box;

use super::measure;
use crate::config::{Config, ProjectCfg, SessionCfg};

pub(super) fn run() {
    for (projects, sessions) in [(1, 32), (8, 256), (16, 256), (32, 256), (64, 10_000)] {
        let config = Config {
            projects: (0..projects)
                .map(|index| ProjectCfg {
                    name: format!("project-{index}"),
                    ..Default::default()
                })
                .collect(),
            ..Default::default()
        };
        let sessions: Vec<_> = (0..sessions)
            .map(|index| SessionCfg {
                project: format!("project-{}", index % projects),
                ..Default::default()
            })
            .collect();
        let expected: usize = sessions
            .iter()
            .filter_map(|session| config.project_of(session))
            .map(|project| project.name.len())
            .sum();
        let label = format!("session projects {projects}x{}", sessions.len());
        measure(&format!("{label} linear"), |_| {
            let config = black_box(&config);
            black_box(&sessions)
                .iter()
                .filter_map(|session| config.project_of(session))
                .map(|project| project.name.len())
                .sum()
        });
        measure(&format!("{label} build+lookup index"), |_| {
            let index = black_box(&config).project_index();
            let found = black_box(&sessions)
                .iter()
                .filter_map(|session| index.get(session.project.as_str()))
                .map(|project| project.name.len())
                .sum();
            assert_eq!(found, expected);
            found
        });
        measure(&format!("{label} snapshot lookup"), |_| {
            let lookup = black_box(&config).project_lookup();
            let found = black_box(&sessions)
                .iter()
                .filter_map(|session| lookup.get(session.project.as_str()))
                .map(|project| project.name.len())
                .sum();
            assert_eq!(found, expected);
            found
        });
    }
}
