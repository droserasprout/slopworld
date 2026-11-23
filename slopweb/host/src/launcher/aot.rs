//! Mixed AOT policy, incremental compiler workspaces, and immutable publication.
//! Preparation owns game patching; this owner consumes only final snapshot assemblies.
use super::{
    cli,
    files::{self, Inputs},
};
use anyhow::{Context, Result, bail, ensure};
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    fs::{self, File, OpenOptions},
    io::{Read, Seek, SeekFrom, Write},
    os::unix::{fs::PermissionsExt, io::AsRawFd, process::CommandExt},
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::Duration,
};
use xmltree::{Element, XMLNode};

const PACKAGES: &[(&str, &str)] = &[
    ("Newtonsoft.Json", "13.0.3"),
    ("Tmds.DBus.Protocol", "0.95.1"),
    ("StbImageSharp", "2.27.13"),
    ("StbImageWriteSharp", "1.16.7"),
];
const INTERPRETED: &[&str] = &[
    "Waylith",
    "Waylith.Injection",
    "RimWorldWaylith.PrepatcherCompat",
];

pub struct Toolchain {
    pub sdk: PathBuf,
    pub identity: Value,
}

fn sdk_command(sdk: &Path) -> Result<Command> {
    let mut command = Command::new(sdk);
    command
        .env(
            "DOTNET_ROOT",
            sdk.parent().context("Missing SDK directory")?,
        )
        .env("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
        .env("DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE", "true")
        .env("DOTNET_NOLOGO", "1");
    Ok(command)
}
fn sdk_selection(version: &str) -> Value {
    json!({"sdk": {"version": version, "rollForward": "disable", "allowPrerelease": false}})
}
fn numeric_version(value: &str, major: &str, minor: Option<&str>) -> bool {
    let parts: Vec<_> = value.split('.').collect();
    parts.len() == 3
        && parts.first() == Some(&major)
        && minor.is_none_or(|minor| parts.get(1) == Some(&minor))
        && parts
            .iter()
            .all(|p| !p.is_empty() && p.bytes().all(|b| b.is_ascii_digit()))
}
fn elements<'a>(root: &'a Element, name: &str, out: &mut Vec<&'a Element>) {
    if root.name == name {
        out.push(root);
    }
    for child in &root.children {
        if let XMLNode::Element(child) = child {
            elements(child, name, out);
        }
    }
}

fn runtime_properties(sdk: &Path, version: &str) -> Result<(String, Vec<u8>)> {
    let bundled = sdk
        .parent()
        .context("Missing SDK directory")?
        .join("sdk")
        .join(version)
        .join("Microsoft.NETCoreSdk.BundledVersions.props");
    let properties = fs::read(&bundled)?;
    let document = Element::parse(properties.as_slice())?;
    let mut runtime_packs = Vec::new();
    elements(&document, "KnownRuntimePack", &mut runtime_packs);
    let runtime = runtime_packs
        .iter()
        .find(|e| {
            e.attributes
                .get("Include")
                .is_some_and(|x| x == "Microsoft.NETCore.App")
                && e.attributes
                    .get("TargetFramework")
                    .is_some_and(|x| x == "net10.0")
        })
        .and_then(|e| e.attributes.get("LatestRuntimeFrameworkVersion"))
        .context("Cannot identify supported AOT target runtime")?
        .clone();
    ensure!(
        numeric_version(&runtime, "10", Some("0")),
        "Unsupported AOT target runtime {runtime}"
    );
    Ok((runtime, properties))
}

fn inspect_packs(sdk: &Path, runtime: &str) -> Result<BTreeMap<String, String>> {
    let packs_root = sdk.parent().context("Missing SDK directory")?.join("packs");
    let mut packs = BTreeMap::new();
    if packs_root.is_dir() {
        for item in fs::read_dir(&packs_root)? {
            let item = item?;
            let name = item.file_name().to_string_lossy().into_owned();
            if (name.starts_with("Microsoft.NET.Runtime.WebAssembly.Sdk")
                || name.starts_with("Microsoft.NET.Sdk.WebAssembly.Pack")
                || name.starts_with("Microsoft.NET.Runtime.Emscripten.")
                || name.ends_with("Cross.browser-wasm")
                || name == "Microsoft.NETCore.App.Runtime.Mono.browser-wasm")
                && item.path().join(runtime).is_dir()
            {
                packs.insert(name, runtime.to_owned());
            }
        }
    }
    Ok(packs)
}
fn compiler_packs_complete(packs: &BTreeMap<String, String>) -> bool {
    packs.keys().any(|name| {
        name.starts_with("Microsoft.NET.Runtime.WebAssembly.Sdk")
            || name.starts_with("Microsoft.NET.Sdk.WebAssembly.Pack")
    }) && packs.keys().any(|name| {
        name.starts_with("Microsoft.NETCore.App.Runtime.AOT.")
            && name.ends_with("Cross.browser-wasm")
    })
}

pub fn toolchain(path: &Path, provision: bool) -> Result<Toolchain> {
    let mut sdk = cli::expand(path)?
        .canonicalize()
        .context("--aot-sdk requires an existing SDK directory or executable dotnet")?;
    if sdk.is_dir() {
        sdk = sdk.join("dotnet").canonicalize()?;
    }
    ensure!(
        sdk.file_name().is_some_and(|n| n == "dotnet")
            && sdk.is_file()
            && fs::metadata(&sdk)?.permissions().mode() & 0o111 != 0,
        "--aot-sdk requires an executable dotnet"
    );
    let temporary = tempfile::Builder::new().prefix("slopweb-sdk-").tempdir()?;
    let query = |args: &[&str]| -> Result<String> {
        let output = sdk_command(&sdk)?
            .args(args)
            .current_dir(temporary.path())
            .output()?;
        ensure!(
            output.status.success(),
            "AOT SDK inspection failed: {} {}\n{}",
            sdk.display(),
            args.join(" "),
            String::from_utf8_lossy(&output.stderr)
        );
        Ok(String::from_utf8(output.stdout)?.trim().into())
    };
    // Let the selected host resolve its latest stable .NET 10 SDK, including
    // installations shared with a newer major. Pin that exact SDK for all work.
    let selection = temporary.path().join("global.json");
    fs::write(
        &selection,
        serde_json::to_vec(&json!({"sdk": {
            "version": "10.0.100", "rollForward": "latestMinor", "allowPrerelease": false
        }}))?,
    )?;
    let version = query(&["--version"])?;
    ensure!(
        numeric_version(&version, "10", None)
            && sdk
                .parent()
                .context("Missing SDK directory")?
                .join("sdk")
                .join(&version)
                .is_dir(),
        "AOT requires an installed .NET 10 SDK; selected {version:?} at {}",
        sdk.display()
    );
    fs::write(selection, serde_json::to_vec(&sdk_selection(&version))?)?;
    let (runtime, properties) = runtime_properties(&sdk, &version)?;
    let workload_present = || -> Result<bool> {
        Ok(query(&["workload", "list"])?
            .lines()
            .any(|line| line.split_whitespace().next() == Some("wasm-tools")))
    };
    let mut packs = inspect_packs(&sdk, &runtime)?;
    if !workload_present()? || !compiler_packs_complete(&packs) {
        ensure!(
            provision,
            "AOT SDK {version} at {} lacks matching wasm-tools compiler packs; install it separately",
            sdk.display()
        );
        let directory = std::ffi::CString::new(
            sdk.parent()
                .context("Missing SDK directory")?
                .as_os_str()
                .as_encoded_bytes(),
        )?;
        // SAFETY: geteuid has no preconditions.
        let uid = unsafe { libc::geteuid() };
        // SAFETY: directory owns the NUL-terminated pathname throughout access.
        let protected = uid != 0 && unsafe { libc::access(directory.as_ptr(), libc::W_OK) } != 0;
        let mut install = if protected {
            let mut sudo = Command::new("sudo");
            sudo.arg(&sdk);
            sudo
        } else {
            sdk_command(&sdk)?
        };
        println!("slopweb: installing matching wasm-tools workload set {version}");
        install
            .args(["workload", "install", "wasm-tools", "--version", &version])
            .current_dir(temporary.path());
        ensure!(
            install.status()?.success(),
            "AOT workload installation failed"
        );
        packs = inspect_packs(&sdk, &runtime)?;
        ensure!(
            workload_present()? && compiler_packs_complete(&packs),
            "AOT workload installation did not provide matching compiler packs"
        );
    }
    let identity = json!({"path": sdk, "version": version, "packs": packs, "target_runtime": runtime, "sdk_properties": files::digest_bytes(&properties)});
    Ok(Toolchain { sdk, identity })
}

pub fn published_loader(main: &str) -> Result<String> {
    let marker = "const assemblyRequest = fetch;\n    const loadAssembly = (bytes, file) => exports.Probe.LoadAssembly(bytes);";
    ensure!(
        main.matches(marker).count() == 1 && !main.contains("LoadPublishedAssembly"),
        "Unrecognized prepared main.js loading boundary"
    );
    let main = main.replace(marker, "const assemblyRequest = null;\n    const loadAssembly = (bytes, file) => exports.Probe.LoadPublishedAssembly(file.replace(/\\.dll$/, ''));");
    ensure!(
        !main.contains("Probe.LoadAssembly("),
        "Unrecognized stream assembly load; refusing mixed identities"
    );
    Ok(main)
}

fn assemblies(snapshot: &Inputs) -> Result<Inputs> {
    for required in [
        "main.js",
        "game/manifest.json",
        "game/Assembly-CSharp.dll",
        "mod/SlopWorld.dll",
        "Adapters.dll",
    ] {
        ensure!(
            snapshot.contains_key(required),
            "Missing final prepared input: {required}"
        );
    }
    let mut dlls = Inputs::new();
    for (relative, path) in snapshot {
        let relative = Path::new(relative);
        if relative.extension().is_some_and(|x| x == "dll")
            && relative.parent().is_some_and(|dir| {
                [Path::new(""), Path::new("game"), Path::new("mod")].contains(&dir)
            })
        {
            let name = relative
                .file_stem()
                .context("Missing assembly name")?
                .to_string_lossy()
                .into_owned();
            ensure!(!dlls.contains_key(&name), "Duplicate assembly name: {name}");
            ensure!(
                name != "Probe" && !PACKAGES.iter().any(|(package, _)| *package == name),
                "Prepared assemblies collide with Probe or pinned package references"
            );
            dlls.insert(name, path.clone());
        }
    }
    ensure!(
        INTERPRETED.iter().all(|name| dlls.contains_key(*name)),
        "Prepared root is missing custom CLR owners"
    );
    Ok(dlls)
}

fn child(name: &str, text: &str) -> Element {
    let mut e = Element::new(name);
    e.children.push(XMLNode::Text(text.into()));
    e
}
fn append(parent: &mut Element, element: Element) {
    parent.children.push(XMLNode::Element(element));
}
fn attr(mut element: Element, name: &str, value: &str) -> Element {
    element.attributes.insert(name.into(), value.into());
    element
}
fn xml_bytes(root: &Element) -> Vec<u8> {
    // xmltree uses a hash map for attributes. Canonical serialization below sorts
    // them so process-randomized iteration never invalidates a compiler cache.
    fn canonical(e: &Element, out: &mut String) {
        fn escape(s: &str) -> String {
            s.replace('&', "&amp;")
                .replace('<', "&lt;")
                .replace('>', "&gt;")
                .replace('"', "&quot;")
        }
        out.push('<');
        out.push_str(&e.name);
        let sorted: BTreeMap<_, _> = e.attributes.iter().collect();
        for (name, value) in sorted {
            out.push(' ');
            out.push_str(name);
            out.push_str("=\"");
            out.push_str(&escape(value));
            out.push('"');
        }
        out.push('>');
        for node in &e.children {
            match node {
                XMLNode::Element(e) => canonical(e, out),
                XMLNode::Text(t) | XMLNode::CData(t) if !t.trim().is_empty() => {
                    out.push_str(&escape(t));
                }
                _ => (),
            }
        }
        out.push_str("</");
        out.push_str(&e.name);
        out.push('>');
    }
    let mut out = String::new();
    canonical(root, &mut out);
    out.into_bytes()
}

/// Compute the exact compiler input manifest before allocating a staging
/// directory. Only a missing publication needs private copies of its inputs.
struct Candidate {
    generated: BTreeMap<String, Vec<u8>>,
    sources: Inputs,
}

impl Candidate {
    fn keys(
        &self,
        contents: &mut files::DigestCache,
        toolchain: &Value,
    ) -> Result<(String, String)> {
        let mut inputs = contents.manifest(&self.sources)?;
        let mut policy = files::Manifest::new();
        for (name, bytes) in &self.generated {
            let digest = files::digest_bytes(bytes);
            if name != "global.json" {
                policy.insert(name.clone(), digest.clone());
            }
            inputs.insert(name.clone(), digest);
        }
        let key = files::digest(&inputs, toolchain)?;
        let workspace_key = files::digest(&policy, toolchain)?;
        Ok((key, workspace_key))
    }

    fn materialize(&self, destination: &Path, contents: &files::DigestCache) -> Result<()> {
        fs::create_dir_all(destination)?;
        for (name, bytes) in &self.generated {
            fs::write(destination.join(name), bytes)?;
        }
        for (name, source) in &self.sources {
            contents.copy_verified(source, &destination.join(name))?;
        }
        Ok(())
    }
}

fn collect_sources(
    e: &mut Element,
    runtime: &Path,
    sources: &mut Inputs,
    seen: &mut BTreeSet<String>,
) -> Result<()> {
    if e.name == "Compile" {
        let name = e
            .attributes
            .get("Include")
            .context("Runtime compile input has no path")?
            .clone();
        ensure!(
            Path::new(&name).file_name().and_then(|n| n.to_str()) == Some(&name)
                && name.ends_with(".cs")
                && !name.contains('*')
                && !name.contains('\\')
                && seen.insert(name.clone()),
            "Probe compiler sources must be distinct explicit local C# filenames"
        );
        let source = runtime.join(&name);
        ensure!(
            !source.is_symlink(),
            "Probe compiler source cannot be a symlink: {name}"
        );
        sources.insert(format!("src/{name}"), source);
        e.attributes.insert("Include".into(), format!("src/{name}"));
    }
    for item in &mut e.children {
        if let XMLNode::Element(item) = item {
            collect_sources(item, runtime, sources, seen)?;
        }
    }
    Ok(())
}

fn interpreter_targets() -> Element {
    let mut targets = Element::new("Project");
    let mut target = attr(
        attr(
            Element::new("Target"),
            "Name",
            "KeepCustomClrOwnersInterpreted",
        ),
        "AfterTargets",
        "_GetDefaultWasmAssembliesToBundle",
    );
    let mut group = Element::new("ItemGroup");
    let condition = INTERPRETED
        .iter()
        .map(|n| format!("'%(Filename)' == '{n}'"))
        .collect::<Vec<_>>()
        .join(" or ");
    let mut item = attr(
        attr(
            Element::new("WasmAssembliesToBundle"),
            "Update",
            "@(WasmAssembliesToBundle)",
        ),
        "Condition",
        &condition,
    );
    append(&mut item, child("AOT_InternalForceToInterpret", "true"));
    append(&mut group, item);
    append(&mut target, group);
    append(&mut targets, target);
    targets
}

fn add_references(project: &mut Element, dlls: &Inputs, sources: &mut Inputs) -> Result<()> {
    let mut group = Element::new("ItemGroup");
    for (name, dll) in dlls {
        let filename = dll.file_name().context("Missing assembly filename")?;
        sources.insert(
            format!("assemblies/{}", filename.to_string_lossy()),
            dll.clone(),
        );
        let mut reference = attr(Element::new("Reference"), "Include", name);
        append(
            &mut reference,
            child(
                "HintPath",
                &format!("assemblies/{}", filename.to_string_lossy()),
            ),
        );
        append(&mut reference, child("Private", "true"));
        append(&mut group, reference);
    }
    let mut declared_packages = Vec::new();
    elements(project, "PackageReference", &mut declared_packages);
    for (name, version) in PACKAGES {
        if let Some(existing) = declared_packages.iter().find(|item| {
            item.attributes
                .get("Include")
                .is_some_and(|value| value == name)
        }) {
            ensure!(
                existing
                    .attributes
                    .get("Version")
                    .is_some_and(|value| value == version),
                "AOT package version differs from authored project: {name}"
            );
            continue;
        }
        append(
            &mut group,
            attr(
                attr(Element::new("PackageReference"), "Include", name),
                "Version",
                version,
            ),
        );
    }
    let mut roots: BTreeSet<&str> = dlls.keys().map(String::as_str).collect();
    roots.insert("Probe");
    roots.extend(PACKAGES.iter().map(|(name, _)| *name));
    for name in roots {
        append(
            &mut group,
            attr(Element::new("TrimmerRootAssembly"), "Include", name),
        );
    }
    append(project, group);
    Ok(())
}

fn candidate(runtime: &Path, dlls: &Inputs, toolchain: &Toolchain) -> Result<Candidate> {
    let runtime_version = toolchain
        .identity
        .get("target_runtime")
        .and_then(Value::as_str)
        .context("Missing target runtime")?;
    let mut sources = Inputs::new();
    let mut project = Element::parse(File::open(runtime.join("Probe.csproj"))?)?;
    let props = project
        .get_mut_child("PropertyGroup")
        .context("Runtime project has no compiler properties")?;
    for (name, value) in [
        ("RunAOTCompilation", "true"),
        ("PublishTrimmed", "true"),
        ("BlazorWebAssemblyEnableLinking", "true"),
        ("WasmStripILAfterAOT", "false"),
        ("JsonSerializerIsReflectionEnabledByDefault", "true"),
        ("RestorePackagesWithLockFile", "false"),
        ("CompressionEnabled", "false"),
        ("BlazorEnableCompression", "false"),
        ("RuntimeFrameworkVersion", runtime_version),
    ] {
        if let Some(existing) = props.get_mut_child(name) {
            existing.children = vec![XMLNode::Text(value.into())];
        } else {
            append(props, child(name, value));
        }
    }
    let mut seen = BTreeSet::new();
    collect_sources(&mut project, runtime, &mut sources, &mut seen)?;
    ensure!(!seen.is_empty(), "Probe must declare its compiler sources");
    add_references(&mut project, dlls, &mut sources)?;
    let targets = interpreter_targets();
    let generated = BTreeMap::from([
        ("Directory.Build.props".into(), b"<Project />\n".to_vec()),
        ("Probe.csproj".into(), xml_bytes(&project)),
        ("Directory.Build.targets".into(), xml_bytes(&targets)),
        (
            "global.json".into(),
            serde_json::to_vec(&sdk_selection(
                toolchain
                    .identity
                    .get("version")
                    .and_then(Value::as_str)
                    .context("Missing SDK version")?,
            ))?,
        ),
    ]);
    Ok(Candidate { generated, sources })
}

pub fn sync_candidate(source: &Path, destination: &Path) -> Result<()> {
    fs::create_dir_all(destination)?;
    for folder in ["src", "assemblies"] {
        for (name, path) in files::files(&destination.join(folder))? {
            if !source.join(folder).join(name).is_file() {
                fs::remove_file(path)?;
            }
        }
    }
    for (name, path) in files::files(source)? {
        let target = destination.join(name);
        let bytes = fs::read(path)?;
        if fs::read(&target).ok().as_deref() != Some(bytes.as_slice()) {
            fs::create_dir_all(target.parent().context("Missing target directory")?)?;
            fs::write(target, bytes)?;
        }
    }
    Ok(())
}

pub fn published_framework(entry: &Path) -> Result<PathBuf> {
    let root = entry.join("published/wwwroot");
    let framework = root.join("_framework");
    let native = if framework.is_dir() {
        fs::read_dir(&framework)?.filter_map(|e| e.ok()).any(|e| {
            let n = e.file_name();
            let n = n.to_string_lossy();
            n.starts_with("dotnet.native") && n.ends_with(".wasm") && e.path().is_file()
        })
    } else {
        false
    };
    ensure!(
        framework.join("dotnet.js").is_file() && native,
        "AOT publish has incomplete framework: {}",
        entry.display()
    );
    Ok(root)
}

pub fn compile_entry(entry: &Path, sdk: &Path, lock: &File, logs: &Path) -> Result<()> {
    let log_path = logs.join("publish.log");
    let mut log = OpenOptions::new()
        .create(true)
        .append(true)
        .open(&log_path)?;
    writeln!(
        log,
        "\n--- Publish attempt {:?} ---",
        std::time::SystemTime::now()
    )?;
    log.flush()?;
    eprintln!("Publishing mixed AOT; log: {}", log_path.display());
    let mut reader = File::open(&log_path)?;
    reader.seek(SeekFrom::End(0))?;
    let stdout = log.try_clone()?;
    let stopped = Arc::new(AtomicBool::new(false));
    let stop = stopped.clone();
    let forward = std::thread::spawn(move || {
        let mut buffer = [0; 8192];
        let mut stderr = std::io::stderr();
        loop {
            match reader.read(&mut buffer) {
                Ok(0) if stop.load(Ordering::Acquire) => break,
                Ok(0) => std::thread::sleep(Duration::from_millis(100)),
                Ok(n) => {
                    if stderr
                        .write_all(buffer.split_at(n).0)
                        .and_then(|()| stderr.flush())
                        .is_err()
                    {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
    });
    let mut command = sdk_command(sdk)?;
    command
        .arg("publish")
        .arg(entry.join("candidate/Probe.csproj"))
        .args(["-c", "Release", "-o"])
        .arg(entry.join("published"))
        .args(["--nologo", "-v", "minimal", "-p:RestoreLockedMode=false"])
        .arg(format!("-bl:{}", logs.join("publish.binlog").display()))
        .current_dir(entry.join("candidate"))
        .stdout(Stdio::from(stdout))
        .stderr(Stdio::from(log));
    inherit_lock(&mut command, lock);
    let status = command.status();
    stopped.store(true, Ordering::Release);
    forward
        .join()
        .map_err(|_panic| anyhow::anyhow!("AOT log reader panicked"))?;
    ensure!(
        status
            .with_context(|| format!("AOT publish failed; see {}", log_path.display()))?
            .success(),
        "AOT publish failed; retry to resume compilation. See {}",
        log_path.display()
    );
    published_framework(entry)?;
    fs::write(entry.join("publish.complete"), "")?;
    Ok(())
}

pub(super) fn inherit_lock(command: &mut Command, lock: &File) {
    let descriptor = lock.as_raw_fd();
    // Change close-on-exec only in the child. The inherited open-file description
    // holds the workspace lock if the launcher is killed while dotnet continues.
    let inherit = move || {
        // SAFETY: F_GETFD takes a descriptor and no additional argument.
        let flags = unsafe { libc::fcntl(descriptor, libc::F_GETFD) };
        if flags < 0 {
            return Err(std::io::Error::last_os_error());
        }
        // SAFETY: F_SETFD takes integer descriptor flags.
        if unsafe { libc::fcntl(descriptor, libc::F_SETFD, flags & !libc::FD_CLOEXEC) } < 0 {
            return Err(std::io::Error::last_os_error());
        }
        Ok(())
    };
    // SAFETY: the child closure uses only async-signal-safe descriptor operations.
    unsafe {
        command.pre_exec(inherit);
    }
}

pub fn prepare(
    bundle: files::PinnedBundle,
    prepared: &Path,
    cache: &Path,
    toolchain: &Toolchain,
) -> Result<PathBuf> {
    let mut snapshot = files::files(prepared)?;
    // The stamp binds the digest to a particular inode. Bind the verified copy below.
    snapshot.remove(crate::archive_identity::MARKER);
    let dlls = assemblies(&snapshot)?;
    let candidate = candidate(&bundle.path().join("aot-src"), &dlls, toolchain)?;
    let mut contents = files::DigestCache::load(cache.join("aot-digests-v1.json"));
    let prepared_files = contents.manifest(&snapshot)?;
    let loader = fs::read_to_string(prepared.join("main.js"))?;
    ensure!(
        Some(&files::digest_bytes(loader.as_bytes())) == prepared_files.get("main.js"),
        "Prepared main.js changed during AOT preparation; retry"
    );
    let main = published_loader(&loader)?;
    let (key, workspace_key) = candidate.keys(&mut contents, &toolchain.identity)?;
    contents.save()?;
    let compilation = cache.join("aot-builds").join(&key);
    fs::create_dir_all(&compilation)?;
    eprintln!("Checking mixed AOT cache: {}", compilation.display());
    // Publication lock spans all later composition, so readers observe one success.
    let _publication_lock = files::lock(&compilation.join("build.lock"))?;
    let needs_publish = !compilation.join("publish.complete").is_file();
    if needs_publish && !compilation.join("candidate").exists() {
        let temporary = tempfile::Builder::new()
            .prefix(".inputs-")
            .tempdir_in(&compilation)?;
        let generated = temporary.path().join("candidate");
        candidate.materialize(&generated, &contents)?;
        fs::rename(generated, compilation.join("candidate"))?;
    }
    // Compilation and composition read only captured files, so staging can resume.
    drop(bundle);
    if needs_publish {
        let workspace = cache.join("aot-workspaces").join(workspace_key);
        fs::create_dir_all(&workspace)?;
        let workspace_lock = files::lock(&workspace.join("build.lock"))?;
        remove_file(&workspace.join("publish.complete"))?;
        sync_candidate(&compilation.join("candidate"), &workspace.join("candidate"))?;
        if workspace.join("published").exists() {
            fs::remove_dir_all(workspace.join("published"))?;
        }
        compile_entry(&workspace, &toolchain.sdk, &workspace_lock, &compilation)?;
        let temporary = tempfile::Builder::new()
            .prefix(".publish-")
            .tempdir_in(&compilation)?;
        let result = temporary.path().join("published");
        files::copy_tree(&workspace.join("published"), &result, |_| false)?;
        if compilation.join("published").exists() {
            fs::remove_dir_all(compilation.join("published"))?;
        }
        fs::rename(result, compilation.join("published"))?;
        published_framework(&compilation)?;
        fs::write(compilation.join("publish.complete"), "")?;
    }
    let published = published_framework(&compilation)?;
    let runtime_key = files::digest(
        &prepared_files,
        &json!({"compilation": key, "loader": main, "archive_identity": 1}),
    )?;
    let runtimes = cache.join("aot-runtimes");
    fs::create_dir_all(&runtimes)?;
    let destination = runtimes.join(&runtime_key);
    if files::ready(&destination, &runtime_key)? {
        return Ok(destination);
    }
    if destination.exists() {
        bail!(
            "Incomplete AOT destination already exists: {}",
            destination.display()
        );
    }
    let temporary = tempfile::Builder::new()
        .prefix(".compose-")
        .tempdir_in(&runtimes)?;
    let root = temporary.path().join("runtime");
    for (name, path) in &snapshot {
        if !name.starts_with("_framework/") {
            contents.copy_verified(path, &root.join(name))?;
        }
    }
    files::copy_tree(&published, &root, |_| false)?;
    fs::write(root.join("main.js"), main)?;
    if let Some(digest) = prepared_files.get(crate::archive_identity::ARCHIVE) {
        crate::archive_identity::publish(&root, digest)?;
    }
    fs::write(root.join(".ready"), runtime_key)?;
    fs::rename(root, &destination)?;
    Ok(destination)
}
fn remove_file(path: &Path) -> Result<()> {
    match fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(e.into()),
    }
}
