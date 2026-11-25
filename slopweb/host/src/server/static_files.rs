//! Static asset delivery. Resolve beneath an open root and stream bounded chunks.
use crate::archive_identity;
use axum::{
    body::Body,
    http::{HeaderMap, Method, StatusCode},
    response::Response,
};
use futures::stream;
use std::{
    ffi::CString,
    fs::{File, Metadata},
    io,
    os::{
        fd::{AsRawFd, FromRawFd},
        unix::ffi::OsStrExt,
    },
    path::{Component, Path},
};
use tokio::io::AsyncReadExt;

use super::reply;

pub(super) async fn response(
    root: &Path,
    path: &str,
    method: &Method,
    headers: &HeaderMap,
) -> Response {
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
    let negotiates = Path::new(&relative).starts_with("_framework")
        && !relative.ends_with(".gz")
        && !relative.ends_with(".br");
    let preferences = negotiates.then(|| EncodingPreferences::from_headers(headers));
    let opened = tokio::task::spawn_blocking(move || open_asset(&root, &name, preferences)).await;
    let asset = match opened {
        Ok(Ok(asset)) => asset,
        Ok(Err(StatusCode::NOT_ACCEPTABLE)) => {
            let mut response = reply(StatusCode::NOT_ACCEPTABLE, "");
            response.headers_mut().insert(
                "vary",
                axum::http::HeaderValue::from_static("Accept-Encoding"),
            );
            return response;
        }
        Ok(Err(status)) => return reply(status, ""),
        Err(_) => return reply(StatusCode::INTERNAL_SERVER_ERROR, ""),
    };
    let etag = &asset.etag;
    let unchanged = headers.get_all("if-none-match").iter().any(|value| {
        value.to_str().is_ok_and(|value| {
            value.split(',').any(|part| {
                let part = part.trim();
                part == "*"
                    || part.strip_prefix("W/").unwrap_or(part)
                        == etag.strip_prefix("W/").unwrap_or(etag)
            })
        })
    });
    let mut response = Response::builder()
        .header("cache-control", cache_control(&relative))
        .header("etag", etag);
    if negotiates {
        response = response.header("vary", "Accept-Encoding");
    }
    if asset.gzip {
        response = response.header("content-encoding", "gzip");
    }
    if unchanged {
        return response
            .status(StatusCode::NOT_MODIFIED)
            .body(Body::empty())
            .unwrap_or_else(|_error| reply(StatusCode::INTERNAL_SERVER_ERROR, ""));
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
        let file = tokio::fs::File::from_std(asset.file);
        Body::from_stream(stream::try_unfold(file, |mut file| async move {
            let mut bytes = vec![0; 64 * 1024];
            let count = file.read(&mut bytes).await?;
            bytes.truncate(count);
            Ok::<_, std::io::Error>((count != 0).then_some((bytes, file)))
        }))
    };
    response
        .status(StatusCode::OK)
        .header("content-type", content_type)
        .header("content-length", asset.encoded_length)
        .header("x-slop-decoded-length", asset.decoded_length)
        .body(body)
        .unwrap_or_else(|_error| reply(StatusCode::INTERNAL_SERVER_ERROR, ""))
}

fn cache_control(relative: &str) -> &'static str {
    // Only the SDK owns this directory. Its fingerprint is a ten-character
    // base36 segment immediately before the resource extension.
    if let Some(file) = relative.strip_prefix("_framework/")
        && !file.contains('/')
        && let Some((stem, "wasm" | "js" | "dat" | "pdb")) = file.rsplit_once('.')
        && let Some((name, fingerprint)) = stem.rsplit_once('.')
        && !name.is_empty()
        && fingerprint.len() == 10
        && fingerprint
            .bytes()
            .all(|byte| byte.is_ascii_lowercase() || byte.is_ascii_digit())
    {
        "private, max-age=31536000, immutable"
    } else {
        "private, no-cache"
    }
}

/// Unlisted identity is available as a fallback. An explicit identity weight
/// competes with gzip; an explicit coding always overrides the wildcard.
#[derive(Default)]
struct EncodingPreferences {
    gzip: Option<u16>,
    identity: Option<u16>,
    wildcard: Option<u16>,
}

impl EncodingPreferences {
    fn from_headers(headers: &HeaderMap) -> Self {
        let mut result = Self::default();
        for value in headers.get_all("accept-encoding") {
            let Ok(value) = value.to_str() else { continue };
            for item in value.split(',') {
                let (coding, parameter) = item.trim().split_once(';').unwrap_or((item.trim(), ""));
                let slot = match coding.trim() {
                    coding if coding.eq_ignore_ascii_case("gzip") => &mut result.gzip,
                    coding if coding.eq_ignore_ascii_case("identity") => &mut result.identity,
                    "*" => &mut result.wildcard,
                    _ => continue,
                };
                let quality = if parameter.is_empty() {
                    1000
                } else {
                    qvalue(parameter).unwrap_or(0)
                };
                *slot = Some(slot.unwrap_or(0).max(quality));
            }
        }
        result
    }

    fn prefers_gzip(&self) -> bool {
        let gzip = self.gzip.or(self.wildcard).unwrap_or(0);
        gzip > 0 && gzip >= self.identity.unwrap_or(0)
    }

    fn allows_identity(&self) -> bool {
        self.identity.map_or(self.wildcard != Some(0), |q| q > 0)
    }
}

// HTTP weights range from 0 to 1 with at most three decimal places.
fn qvalue(parameter: &str) -> Option<u16> {
    let (name, value) = parameter.trim().split_once('=')?;
    if !name.trim().eq_ignore_ascii_case("q") {
        return None;
    }
    let value = value.trim();
    let (whole, fraction) = value.split_once('.').unwrap_or((value, ""));
    if fraction.len() > 3 || !fraction.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    let millis = fraction
        .bytes()
        .zip([100, 10, 1])
        .map(|(b, weight)| u16::from(b - b'0') * weight)
        .sum::<u16>();
    match whole {
        "0" => Some(millis),
        "1" if millis == 0 => Some(1000),
        _ => None,
    }
}

struct Asset {
    file: File,
    encoded_length: u64,
    decoded_length: u64,
    gzip: bool,
    etag: String,
}

fn regular_file(root: &File, name: &str) -> Result<(File, Metadata), StatusCode> {
    let file = open_beneath(root, name).map_err(|error| match error.raw_os_error() {
        Some(libc::EXDEV | libc::ELOOP | libc::EACCES) => StatusCode::FORBIDDEN,
        _ => StatusCode::NOT_FOUND,
    })?;
    let metadata = file.metadata().map_err(|_error| StatusCode::NOT_FOUND)?;
    if !metadata.is_file() {
        return Err(StatusCode::FORBIDDEN);
    }
    Ok((file, metadata))
}

fn open_asset(
    root: &Path,
    name: &str,
    preferences: Option<EncodingPreferences>,
) -> Result<Asset, StatusCode> {
    // Resolve identity, representation and validator against the same open root.
    let root = File::open(root).map_err(|_error| StatusCode::NOT_FOUND)?;
    let (file, metadata) = regular_file(&root, name)?;
    let decoded_length = metadata.len();
    let stamp = archive_identity::metadata_stamp(&metadata);
    let mut etag = format!("W/\"{stamp}\"");
    if let Some(preferences) = preferences {
        if preferences.prefers_gzip() {
            match regular_file(&root, &format!("{name}.gz")) {
                Ok((file, compressed)) => {
                    let gzip_stamp = archive_identity::metadata_stamp(&compressed);
                    return Ok(Asset {
                        file,
                        encoded_length: compressed.len(),
                        decoded_length,
                        gzip: true,
                        etag: format!("W/\"gzip-{stamp}-{gzip_stamp}\""),
                    });
                }
                Err(StatusCode::NOT_FOUND) => (),
                Err(status) => return Err(status),
            }
        }
        if !preferences.allows_identity() {
            return Err(StatusCode::NOT_ACCEPTABLE);
        }
    } else if Path::new(name) == Path::new(archive_identity::ARCHIVE)
        && let Ok(marker) = open_beneath(&root, archive_identity::MARKER)
        && let Some(tag) = archive_identity::content_tag(marker, &stamp)
    {
        etag = tag;
    }
    Ok(Asset {
        file,
        encoded_length: decoded_length,
        decoded_length,
        gzip: false,
        etag,
    })
}

fn open_beneath(root: &File, relative: &str) -> io::Result<File> {
    let path =
        CString::new(relative).map_err(|_error| std::io::Error::from_raw_os_error(libc::EINVAL))?;
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
        let error = io::Error::last_os_error();
        if error.raw_os_error() == Some(libc::ENOSYS) {
            // RestrictSUIDSGID can mask openat2 even for read-only opens.
            return open_without_symlinks(root, Path::new(relative));
        }
        return Err(error);
    }
    let fd = i32::try_from(fd).map_err(std::io::Error::other)?;
    // SAFETY: the successful syscall transfers a fresh owned descriptor.
    Ok(unsafe { std::fs::File::from_raw_fd(fd) })
}

// Each descriptor pins the directory used for the next lookup. Reject symlinks
// at every step when the kernel cannot enforce RESOLVE_BENEATH itself.
pub(super) fn open_without_symlinks(root: &File, relative: &Path) -> io::Result<File> {
    let mut directory = root.try_clone()?;
    let mut components = relative.components().peekable();
    while let Some(component) = components.next() {
        let name = match component {
            Component::CurDir => continue,
            Component::Normal(name) => CString::new(name.as_bytes())?,
            _ => return Err(io::Error::from_raw_os_error(libc::EACCES)),
        };
        let mut flags = libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_CLOEXEC | libc::O_NONBLOCK;
        if components.peek().is_some() {
            flags |= libc::O_DIRECTORY;
        }
        // SAFETY: the borrowed directory and NUL-terminated component are live.
        let fd = unsafe { libc::openat(directory.as_raw_fd(), name.as_ptr(), flags) };
        if fd < 0 {
            return Err(io::Error::last_os_error());
        }
        // SAFETY: successful openat transfers ownership of the new descriptor.
        directory = unsafe { File::from_raw_fd(fd) };
    }
    Ok(directory)
}
