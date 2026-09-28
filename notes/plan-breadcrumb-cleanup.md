# Remove dormant YOLO breadcrumb injection

Status: implemented

Automatic first-Enter injection retained state and input/lifecycle hooks after its only
production producer was removed. Remove those hooks and the unused key-message tip list.

Keep manual library breadcrumb rendering and paste without submission. Keep worker/errand
prompt delivery as readiness, paste, delay, Enter. Future breadcrumb lists belong to prompt
construction before delivery, not hidden state consumed by an arbitrary keypress.

Review the input and library delivery changes with their tests. The former pending-state
tests are replaced by manual-paste and worker/errand submission regressions. Human review
is pending; ownership guidance lives in [session state](daemon-session-state.md) and
[workers](daemon-workers.md).
