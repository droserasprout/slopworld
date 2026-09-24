# Automatic terminal input suite and Markdown report
Status: implemented

User-requested workflow: one empty host shell, bounded alphanumeric random output
fills scrollback, history test, Unicode append test, then a generated Markdown
note. Use one preparation prompt. Keep focus through both phases. Retain standalone
manual phase runs and allow report-only conversion of existing artifacts.

Verify finite printable output, shell-command quoting, paste-before-Enter ordering,
setup failure/focus loss, automatic phase transitions, and conservative reporting
of censored measurements. Setup is outside measured windows. Do not launch the
game or inject desktop input during automated validation.

Implemented: default `--phase suite` prepares one tab and runs history/typing
without another prompt. Manual phases remain available. Markdown reports include
outcomes, p50/p95/p99/max, FPS and work by context, artifact links, and measurement
limits. The report withholds percentiles for censored runs. Existing run 5 now has a generated summary note.

Validation: supporting-tool checks passed; 27 focused runner/suite tests cover
finite printable generation, standalone shell quoting, an isolated real tmux pane,
paste acknowledgement before Enter, unattended phase ordering, focus/setup failures,
and censored or invalid reporting. Validation did not use the game or desktop input.
The subsequent human-run automatic suite completed preparation and both input phases.

In-game validation: [the suite report](../bench/terminal-input/reports/perf-suite-terminal-input-next.md)
records runner e8237232.
Preparation generated 10,050 lines at 120 columns for the 10,000-line target.
History sent 36,000 events with 4.297 ms of schedule slip. Superseded movements
make its latency distribution partial. The report therefore withholds headline percentiles.
Typing sent and completed all 6,000 requests. The p50 was 93.710 ms, and the p99
was 165.410 ms. Mean FPS was 26.96. The report and outcome classifications were written automatically.
