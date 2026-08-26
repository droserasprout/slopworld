use axum::extract::Request;
use axum::http::StatusCode;
use axum::middleware::{self, Next};
use axum::response::{IntoResponse, Response};
use axum::routing::{delete, get, post, put};
use axum::{Extension, Router};

use crate::grant::Cap;

use super::handlers::*;
use super::ws::ws_upgrade;
use super::{err, Mgr};

pub(crate) fn router(m: Mgr) -> Router {
    // Reachable by a scoped grant as well as the root. Each handler checks its own session and
    // level (`guard`), the list is filtered, and `/ws` gates every message - so a grant reaches
    // exactly the sessions it names, at the level it was given, and the host through none of it.
    // Creating a session (`POST /api/sessions`) is root-only even here, by `guard_create`.
    let scoped = Router::new()
        .route("/api/health", get(health))
        .route("/api/sessions", get(list).post(create))
        .route("/api/sessions/:name", get(one).put(update).delete(destroy))
        .route("/api/sessions/:name/start", post(start))
        .route("/api/sessions/:name/stop", post(stop))
        .route("/api/sessions/:name/restart", post(restart))
        .route("/api/sessions/:name/label", put(set_label))
        .route("/api/sessions/:name/state/reset", post(reset_state))
        .route(
            "/api/tasks",
            get(list_tasks).post(create_task).delete(prune_tasks),
        )
        .route(
            "/api/tasks/:id",
            get(one_task).post(update_task).delete(remove_task),
        )
        .route("/ws", get(ws_upgrade));

    // The mod's own: the file, the projects, the machine, and minting grants itself. Default
    // deny - a grant reaches none of it, and a route added here is root-only until someone
    // moves it into `scoped` on purpose. The layer reads the capability `auth` already hung on
    // the request.
    let root = Router::new()
        .route("/api/capabilities", get(capabilities))
        .route("/api/projects", get(list_projects).post(create_project))
        .route(
            "/api/projects/:name",
            get(one_project).put(update_project).delete(destroy_project),
        )
        .route("/api/shortcuts", get(list_shortcuts).post(create_shortcut))
        .route(
            "/api/shortcuts/:name",
            put(update_shortcut).delete(destroy_shortcut),
        )
        .route("/api/shortcuts/:name/run", post(run_shortcut))
        .route("/api/run", post(run))
        .route("/api/file-action", post(file_action))
        .route("/api/open-apps", get(open_apps))
        .route("/api/grants", get(list_grants).post(mint_grant))
        .route("/api/grants/:grantor", delete(revoke_grants))
        .route("/api/state", get(stored_states))
        .route("/api/state/:kind/:key", delete(delete_stored_state))
        .route("/api/state/trash/:key/restore", post(restore_stored_state))
        .route("/api/presets", get(presets))
        .route(
            "/api/presets/:kind/:name",
            put(update_preset).delete(delete_preset),
        )
        .route("/api/presets/:kind/:name/copy", post(copy_preset))
        .route("/api/config", get(get_config))
        .route("/api/config", put(put_config))
        .route("/api/config/patch", put(put_config_patch))
        .route("/api/clipboard", get(clip_read).post(clip_write))
        .route("/api/clipboard/text", get(clip_read_text))
        .route("/api/clipboard/primary", get(clip_read_primary))
        .route("/api/clipboard/primary/text", get(clip_read_primary_text))
        .route("/api/usage", get(usage))
        .route("/api/audio", get(audio))
        .route("/api/jukebox", get(jukebox))
        .route("/api/browse", get(browse))
        .route("/api/read", get(read_file))
        .route("/api/highlight", post(highlight))
        .route("/api/image", get(read_image))
        .route(
            "/api/files",
            post(create_file).put(rename_file).delete(remove_file),
        )
        .route("/api/search", get(search))
        .route("/api/git", get(git_status))
        .layer(middleware::from_fn(require_root));

    scoped.merge(root).with_state(m)
}

/// The default-deny half of the API: everything but the session read/drive routes is the mod's,
/// so a scoped grant gets 403 here before a handler runs. The capability was resolved and hung
/// on the request by `auth`, which is the outer layer, so it is always present by now.
async fn require_root(Extension(cap): Extension<Cap>, req: Request, next: Next) -> Response {
    if cap.may_create() {
        next.run(req).await
    } else {
        err(
            StatusCode::FORBIDDEN,
            "this route needs the daemon's own token",
        )
        .into_response()
    }
}
