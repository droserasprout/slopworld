//! Static asset delivery. Resolve beneath an open root and stream bounded chunks.
use axum::{
    body::Body,
    http::{Method, StatusCode},
    response::Response,
};
use futures::stream;
use std::{
    ffi::CString,
    os::fd::{AsRawFd, FromRawFd},
    path::{Component, Path},
};
use tokio::io::AsyncReadExt;

use super::reply;

pub(super) async fn response(root: &Path, path: &str, method: &Method) -> Response {
    if !matches!(*method, Method::GET | Method::HEAD) {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let Ok(decoded) = percent_encoding::percent_decode_str(path).decode_utf8() else {
        return reply(StatusCode::NOT_FOUND, "");
    };
    let mut relative = decoded.trim_start_matches('/').to_owned();
    if decoded.ends_with('/') {
        relative.push_str("index.html");
    }
    if Path::new(&relative)
        .components()
        .any(|part| !matches!(part, Component::Normal(_) | Component::CurDir))
    {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let root = root.to_owned();
    let name = relative.clone();
    let opened = tokio::task::spawn_blocking(move || open_beneath(&root, &name)).await;
    let file = match opened {
        Ok(Ok(file)) => file,
        Ok(Err(error))
            if matches!(
                error.raw_os_error(),
                Some(libc::EXDEV | libc::ELOOP | libc::EACCES)
            ) =>
        {
            return reply(StatusCode::FORBIDDEN, "");
        }
        _ => return reply(StatusCode::NOT_FOUND, ""),
    };
    let Ok(metadata) = file.metadata() else {
        return reply(StatusCode::NOT_FOUND, "");
    };
    if !metadata.is_file() {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let content_type = match Path::new(&relative)
        .extension()
        .and_then(|value| value.to_str())
    {
        Some("html") => "text/html",
        Some("js" | "mjs") => "text/javascript",
        Some("json") => "application/json",
        Some("wasm") => "application/wasm",
        Some("css") => "text/css",
        _ => "application/octet-stream",
    };
    let body = if *method == Method::HEAD {
        Body::empty()
    } else {
        let file = tokio::fs::File::from_std(file);
        Body::from_stream(stream::try_unfold(file, |mut file| async move {
            let mut bytes = vec![0; 64 * 1024];
            let count = file.read(&mut bytes).await?;
            bytes.truncate(count);
            Ok::<_, std::io::Error>((count != 0).then_some((bytes, file)))
        }))
    };
    Response::builder()
        .status(StatusCode::OK)
        .header("content-type", content_type)
        .header("content-length", metadata.len())
        .body(body)
        .unwrap()
}

fn open_beneath(root: &Path, relative: &str) -> std::io::Result<std::fs::File> {
    let root = std::fs::File::open(root)?;
    let path =
        CString::new(relative).map_err(|_| std::io::Error::from_raw_os_error(libc::EINVAL))?;
    // openat2 makes containment atomic even if a directory or symlink changes
    // during admission. Internal symlinks remain useful; escaping links fail closed.
    #[repr(C)]
    struct OpenHow {
        flags: u64,
        mode: u64,
        resolve: u64,
    }
    let how = OpenHow {
        flags: (libc::O_RDONLY | libc::O_CLOEXEC | libc::O_NONBLOCK) as u64,
        mode: 0,
        resolve: 0x08 | 0x02,
    };
    // SAFETY: openat2 receives live descriptors and pointers for the duration of the call.
    let fd = unsafe {
        libc::syscall(
            libc::SYS_openat2,
            root.as_raw_fd(),
            path.as_ptr(),
            &how,
            std::mem::size_of::<OpenHow>(),
        )
    };
    if fd < 0 {
        return Err(std::io::Error::last_os_error());
    }
    // SAFETY: the successful syscall transfers a fresh owned descriptor.
    Ok(unsafe { std::fs::File::from_raw_fd(fd as i32) })
}
