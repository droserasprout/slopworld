//! Linux Fontconfig enumeration and monochrome ordering. Dynamic linking needs only
//! the runtime library; literal family names never pass through pattern syntax.
use anyhow::{Context, Result, ensure};
use std::{
    ffi::{CStr, CString, c_char, c_int, c_void},
    path::PathBuf,
    sync::{Mutex, OnceLock},
};

type Ptr = *mut c_void;
#[repr(C)]
struct FontSet {
    count: c_int,
    capacity: c_int,
    fonts: *mut Ptr,
}
static GATE: Mutex<()> = Mutex::new(());
static LIB: OnceLock<usize> = OnceLock::new();
fn library() -> Result<Ptr> {
    let handle = *LIB.get_or_init(|| unsafe {
        libc::dlopen(
            c"libfontconfig.so.1".as_ptr(),
            libc::RTLD_NOW | libc::RTLD_LOCAL,
        ) as usize
    });
    ensure!(handle != 0, "Could not load libfontconfig.so.1");
    Ok(handle as Ptr)
}
macro_rules! call {
    ($name:literal, $ty:ty $(, $arg:expr)*) => {{
        let symbol=unsafe {libc::dlsym(library()?,concat!($name,"\0").as_ptr().cast())};
        ensure!(!symbol.is_null(),"Missing Fontconfig symbol {}",$name);
        let function:$ty=unsafe {std::mem::transmute(symbol)};
        unsafe {function($($arg),*)}
    }};
}
struct Pattern(Ptr);
impl Drop for Pattern {
    fn drop(&mut self) {
        let _ = destroy_pattern(self.0);
    }
}
fn destroy_pattern(p: Ptr) -> Result<()> {
    call!("FcPatternDestroy", unsafe extern "C" fn(Ptr), p);
    Ok(())
}
struct Set(Ptr);
impl Drop for Set {
    fn drop(&mut self) {
        let _ = destroy_set(self.0);
    }
}
fn destroy_set(p: Ptr) -> Result<()> {
    call!("FcFontSetDestroy", unsafe extern "C" fn(Ptr), p);
    Ok(())
}
fn string(p: Ptr, property: &CStr) -> Result<Option<String>> {
    let mut value: *mut c_char = std::ptr::null_mut();
    let result = call!(
        "FcPatternGetString",
        unsafe extern "C" fn(Ptr, *const c_char, c_int, *mut *mut c_char) -> c_int,
        p,
        property.as_ptr(),
        0,
        &mut value
    );
    Ok((result == 0 && !value.is_null())
        .then(|| unsafe { CStr::from_ptr(value).to_string_lossy().into_owned() }))
}
fn entries(set: &Set) -> &[Ptr] {
    let s = unsafe { &*set.0.cast::<FontSet>() };
    if s.count <= 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(s.fonts, s.count as usize) }
    }
}
fn initialize() -> Result<()> {
    ensure!(
        call!("FcInitBringUptoDate", unsafe extern "C" fn() -> c_int) != 0,
        "Fontconfig initialization failed"
    );
    Ok(())
}

pub(super) fn inventory() -> Result<(Vec<PathBuf>, Vec<PathBuf>)> {
    let _guard = GATE.lock().unwrap();
    // Reinitialization observes changes immediately, independent of Fontconfig's
    // rescan interval. Called only after our persisted metadata invalidates.
    ensure!(
        call!("FcInitReinitialize", unsafe extern "C" fn() -> c_int) != 0,
        "Fontconfig reload failed"
    );
    let pattern = Pattern(call!("FcPatternCreate", unsafe extern "C" fn() -> Ptr));
    ensure!(!pattern.0.is_null(), "Fontconfig allocation failed");
    let set = Set(call!(
        "FcFontList",
        unsafe extern "C" fn(Ptr, Ptr, Ptr) -> Ptr,
        std::ptr::null_mut(),
        pattern.0,
        std::ptr::null_mut()
    ));
    ensure!(!set.0.is_null(), "Fontconfig inventory failed");
    let mut files = Vec::new();
    for &p in entries(&set) {
        if let Some(path) = string(p, c"file")? {
            files.push(PathBuf::from(path));
        }
    }
    files.sort_by_key(|p| p.to_string_lossy().to_lowercase());
    files.dedup();
    let mut watches = Vec::new();
    let config = call!("FcConfigGetCurrent", unsafe extern "C" fn() -> Ptr);
    for name in [
        c"FcConfigGetFontDirs",
        c"FcConfigGetConfigFiles",
        c"FcConfigGetConfigDirs",
    ] {
        let symbol = unsafe { libc::dlsym(library()?, name.as_ptr()) };
        ensure!(!symbol.is_null(), "Missing Fontconfig config accessor");
        let get: unsafe extern "C" fn(Ptr) -> Ptr = unsafe { std::mem::transmute(symbol) };
        let list = unsafe { get(config) };
        if list.is_null() {
            continue;
        }
        loop {
            let next = call!(
                "FcStrListNext",
                unsafe extern "C" fn(Ptr) -> *const c_char,
                list
            );
            if next.is_null() {
                break;
            }
            watches.push(PathBuf::from(
                unsafe { CStr::from_ptr(next) }
                    .to_string_lossy()
                    .into_owned(),
            ));
        }
        call!("FcStrListDone", unsafe extern "C" fn(Ptr), list);
    }
    Ok((files, watches))
}
pub(super) fn order(family: &str) -> Result<Vec<String>> {
    let _guard = GATE.lock().unwrap();
    initialize()?;
    let family = CString::new(family).context("Invalid family")?;
    let p = Pattern(call!("FcPatternCreate", unsafe extern "C" fn() -> Ptr));
    ensure!(!p.0.is_null(), "Fontconfig allocation failed");
    ensure!(
        call!(
            "FcPatternAddString",
            unsafe extern "C" fn(Ptr, *const c_char, *const c_char) -> c_int,
            p.0,
            c"family".as_ptr(),
            family.as_ptr()
        ) != 0,
        "Fontconfig family failed"
    );
    ensure!(
        call!(
            "FcConfigSubstitute",
            unsafe extern "C" fn(Ptr, Ptr, c_int) -> c_int,
            std::ptr::null_mut(),
            p.0,
            0
        ) != 0,
        "Fontconfig substitute failed"
    );
    call!("FcDefaultSubstitute", unsafe extern "C" fn(Ptr), p.0);
    let mut result = 0;
    let set = Set(call!(
        "FcFontSort",
        unsafe extern "C" fn(Ptr, Ptr, c_int, Ptr, *mut c_int) -> Ptr,
        std::ptr::null_mut(),
        p.0,
        0,
        std::ptr::null_mut(),
        &mut result
    ));
    ensure!(!set.0.is_null() && result == 0, "Fontconfig sort failed");
    let mut names = Vec::new();
    for &font in entries(&set) {
        let mut color = 0;
        if call!(
            "FcPatternGetBool",
            unsafe extern "C" fn(Ptr, *const c_char, c_int, *mut c_int) -> c_int,
            font,
            c"color".as_ptr(),
            0,
            &mut color
        ) == 0
            && color != 0
        {
            continue;
        }
        if let Some(name) = string(font, c"family")? {
            names.push(name);
        }
    }
    Ok(names)
}
