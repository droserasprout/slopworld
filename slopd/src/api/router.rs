use axum::extract::Request;
use axum::http::StatusCode;
use axum::middleware::{self, Next};
use axum::response::{IntoResponse, Response};
use axum::routing::{delete, get, post, put};
use axum::{Extension, Router};

use crate::grant::Cap;
use crate::wire::routes;

use super::handlers::*;
use super::ws::ws_upgrade;
use super::{err, Mgr};

pub(crate) fn router(m: Mgr) -> Router {
    // Reachable by a scoped grant as well as the root. Each handler checks its own session and
    // level (`guard`), the default task list is filtered, and `/ws` gates every message - so a
    // grant reaches exactly the sessions it names, at the level it was given, and the host through
    // none of it. Root may opt into the complete task board with `?all=true`.
    // Creating a session (`POST /api/sessions`) is root-only even here, by `guard_create`.
    let scoped = Router::new()
        .route(routes::HEALTH, get(health))
        .route(routes::SESSIONS, get(list).post(create))
        .route(routes::SESSION, get(one).put(update).delete(destroy))
        .route(routes::SESSION_CWD, get(cwd))
        .route(routes::SESSION_START, post(start))
        .route(routes::SESSION_STOP, post(stop))
        .route(routes::SESSION_RESTART, post(restart))
        .route(routes::SESSION_LABEL, put(set_label))
        .route(routes::SESSION_STATE_RESET, post(reset_state))
        .route(
            routes::TASKS,
            get(list_tasks).post(create_task).delete(prune_tasks),
        )
        .route(routes::TASKS_CANCEL, post(cancel_tasks))
        .route(routes::TASKS_REMOVE, post(remove_tasks))
        .route(
            routes::TASK,
            get(one_task).post(update_task).delete(remove_task),
        )
        .route(crate::wire::WS_PATH, get(ws_upgrade));

    // The mod's own: the file, the projects, the machine, and minting grants itself. Default
    // deny - a grant reaches none of it, and a route added here is root-only until someone
    // moves it into `scoped` on purpose. The layer reads the capability `auth` already hung on
    // the request.
    let root = Router::new()
        .route(routes::CAPABILITIES, get(capabilities))
        .route(routes::PROJECTS, get(list_projects).post(create_project))
        .route(
            routes::PROJECT,
            get(one_project).put(update_project).delete(destroy_project),
        )
        .route(routes::LIBRARY, get(list_library).post(create_library_item))
        .route(
            routes::LIBRARY_ITEM,
            put(update_library_item).delete(destroy_library_item),
        )
        .route(routes::LIBRARY_ITEM_RUN, post(run_library_item))
        .route(routes::RUN, post(run))
        .route(routes::WORKERS, post(spawn_worker))
        .route(routes::FILE_ACTION, post(file_action))
        .route(routes::OPEN_APPS, get(open_apps))
        .route(routes::GRANTS, get(list_grants).post(mint_grant))
        .route(routes::GRANTOR, delete(revoke_grants))
        .route(routes::STATE, get(stored_states))
        .route(routes::STATE_TRASH, delete(empty_trash))
        .route(routes::STATE_ITEM, delete(delete_stored_state))
        .route(routes::STATE_TRASH_RESTORE, post(restore_stored_state))
        .route(routes::PRESETS, get(presets))
        .route(routes::PRESET, put(update_preset).delete(delete_preset))
        .route(routes::PRESET_COPY, post(copy_preset))
        .route(routes::CONFIG, get(get_config))
        .route(routes::CONFIG, put(put_config))
        .route(routes::CONFIG_PATCH, put(put_config_patch))
        .route(routes::INSTRUCTIONS_PREVIEW, post(instructions_preview))
        .route(routes::CLIPBOARD, get(clip_read).post(clip_write))
        .route(routes::CLIPBOARD_TEXT, get(clip_read_text))
        .route(
            routes::CLIPBOARD_PRIMARY,
            get(clip_read_primary).post(clip_write_primary),
        )
        .route(routes::CLIPBOARD_PRIMARY_TEXT, get(clip_read_primary_text))
        .route(routes::USAGE, get(usage))
        .route(routes::AUDIO, get(audio))
        .route(routes::JUKEBOX, get(jukebox))
        .route(routes::BROWSE, get(browse))
        .route(routes::READ, get(read_file))
        .route(routes::HIGHLIGHT, post(highlight))
        .route(routes::IMAGE, get(read_image))
        .route(
            routes::FILES,
            post(create_file).put(rename_file).delete(remove_file),
        )
        .route(routes::SEARCH, get(search))
        .route(routes::GIT, get(git_status))
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
