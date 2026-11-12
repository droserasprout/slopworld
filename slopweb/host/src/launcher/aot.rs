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

fn sdk_command(sdk: &Path) -> Command {
    let mut command = Command::new(sdk);
    command
        .env("DOTNET_ROOT", sdk.parent().unwrap())
        .env("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
        .env("DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE", "true")
        .env("DOTNET_NOLOGO", "1");
    command
}
fn sdk_selection(version: &str) -> Value {
    json!({"sdk": {"version": version, "rollForward": "disable", "allowPrerelease": false}})
}
fn numeric_version(value: &str, major: &str, minor: Option<&str>) -> bool {
    let parts: Vec<_> = value.split('.').collect();
    parts.len() == 3
        && parts[0] == major
        && minor.is_none_or(|minor| parts[1] == minor)
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
        let output = sdk_command(&sdk)
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
    let version = query(&["--version"])?;
    ensure!(
        numeric_version(&version, "10", None)
            && sdk.parent().unwrap().join("sdk").join(&version).is_dir(),
        "AOT requires an installed .NET 10 SDK; selected {version:?} at {}",
        sdk.display()
    );
    fs::write(
        temporary.path().join("global.json"),
        serde_json::to_vec(&sdk_selection(&version))?,
    )?;
    let bundled = sdk
        .parent()
        .unwrap()
        .join("sdk")
        .join(&version)
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
    let packs_root = sdk.parent().unwrap().join("packs");
    let inspect_packs = || -> Result<BTreeMap<String, String>> {
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
                    && item.path().join(&runtime).is_dir()
                {
                    packs.insert(name, runtime.clone());
                }
            }
        }
        Ok(packs)
    };
    let complete = |packs: &BTreeMap<String, String>| {
        packs.keys().any(|name| {
            name.starts_with("Microsoft.NET.Runtime.WebAssembly.Sdk")
                || name.starts_with("Microsoft.NET.Sdk.WebAssembly.Pack")
        }) && packs.keys().any(|name| {
            name.starts_with("Microsoft.NETCore.App.Runtime.AOT.")
                && name.ends_with("Cross.browser-wasm")
        })
    };
    let workload_present = || -> Result<bool> {
        Ok(query(&["workload", "list"])?
            .lines()
            .any(|line| line.split_whitespace().next() == Some("wasm-tools")))
    };
    let mut packs = inspect_packs()?;
    if !workload_present()? || !complete(&packs) {
        ensure!(
            provision,
            "AOT SDK {version} at {} lacks matching wasm-tools compiler packs; install it separately",
            sdk.display()
        );
        let directory =
            std::ffi::CString::new(sdk.parent().unwrap().as_os_str().as_encoded_bytes())?;
        let protected =
            unsafe { libc::geteuid() != 0 && libc::access(directory.as_ptr(), libc::W_OK) != 0 };
        let mut install = if protected {
            let mut sudo = Command::new("sudo");
            sudo.arg(&sdk);
            sudo
        } else {
            sdk_command(&sdk)
        };
        println!("slopweb: installing matching wasm-tools workload set {version}");
        install
            .args(["workload", "install", "wasm-tools", "--version", &version])
            .current_dir(temporary.path());
        ensure!(
            install.status()?.success(),
            "AOT workload installation failed"
        );
        packs = inspect_packs()?;
        ensure!(
            workload_present()? && complete(&packs),
            "AOT workload installation did not provide matching compiler packs"
        );
    }
    let identity = json!({"path": sdk, "version": version, "packs": packs, "target_runtime": runtime, "sdk_properties": files::digest_bytes(&properties)});
    Ok(Toolchain { sdk, identity })
}

pub fn published_loader(main: &str) -> Result<String> {
    let marker = "    const loaded = [];";
    ensure!(
        main.matches(marker).count() == 1 && !main.contains("LoadPublishedAssembly"),
        "Unrecognized prepared main.js loading boundary"
    );
    let mut main = main.replace(marker, &format!("{marker}\n    const loadPublished = response => exports.Probe.LoadPublishedAssembly(decodeURIComponent(new URL(response.url).pathname.split('/').pop()).replace(/\\.dll$/, ''));"));
    for variable in ["response", "adapters"] {
        main = main.replace(
            &format!("exports.Probe.LoadAssembly(new Uint8Array(await {variable}.arrayBuffer()))"),
            &format!("loadPublished({variable})"),
        );
    }
    ensure!(
        !main.contains("Probe.LoadAssembly(") && main.contains("loadPublished(response)"),
        "Unrecognized stream assembly load; refusing mixed identities"
    );
    Ok(main)
}

pub fn assemblies(prepared: &Path) -> Result<Inputs> {
    for required in [
        "main.js",
        "game/manifest.json",
        "game/Assembly-CSharp.dll",
        "mod/SlopWorld.dll",
        "Adapters.dll",
    ] {
        ensure!(
            prepared.join(required).is_file(),
            "Missing final prepared input: {required}"
        );
    }
    files::files(prepared)?; // Reject aliases before copying any host bytes.
    let mut dlls = Inputs::new();
    for folder in [prepared.join("game"), prepared.join("mod"), prepared.into()] {
        for item in fs::read_dir(folder)? {
            let path = item?.path();
            if path.is_file() && path.extension().is_some_and(|x| x == "dll") {
                let name = path.file_stem().unwrap().to_string_lossy().into_owned();
                ensure!(!dlls.contains_key(&name), "Duplicate assembly name: {name}");
                ensure!(
                    name != "Probe" && !PACKAGES.iter().any(|(package, _)| *package == name),
                    "Prepared assemblies collide with Probe or pinned package references"
                );
                dlls.insert(name, path);
            }
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
fn write_xml(path: &Path, root: &Element) -> Result<()> {
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
    fs::write(path, out)?;
    Ok(())
}

pub fn write_candidate(
    runtime: &Path,
    candidate: &Path,
    dlls: &Inputs,
    runtime_version: &str,
) -> Result<()> {
    fs::create_dir_all(candidate.join("assemblies"))?;
    fs::create_dir_all(candidate.join("src"))?;
    fs::write(candidate.join("Directory.Build.props"), "<Project />\n")?;
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
    fn copy_sources(
        e: &mut Element,
        runtime: &Path,
        candidate: &Path,
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
            fs::copy(runtime.join(&name), candidate.join("src").join(&name))?;
            e.attributes.insert("Include".into(), format!("src/{name}"));
        }
        for item in &mut e.children {
            if let XMLNode::Element(item) = item {
                copy_sources(item, runtime, candidate, seen)?;
            }
        }
        Ok(())
    }
    let mut seen = BTreeSet::new();
    copy_sources(&mut project, runtime, candidate, &mut seen)?;
    ensure!(!seen.is_empty(), "Probe must declare its compiler sources");
    let mut group = Element::new("ItemGroup");
    for (name, dll) in dlls {
        let filename = dll.file_name().unwrap();
        fs::copy(dll, candidate.join("assemblies").join(filename))?;
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
    for (name, version) in PACKAGES {
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
    append(&mut project, group);
    write_xml(&candidate.join("Probe.csproj"), &project)?;
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
    write_xml(&candidate.join("Directory.Build.targets"), &targets)
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
            fs::create_dir_all(target.parent().unwrap())?;
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
                        .write_all(&buffer[..n])
                        .and_then(|_| stderr.flush())
                        .is_err()
                    {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
    });
    let mut command = sdk_command(sdk);
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
    let _ = forward.join();
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
    unsafe {
        command.pre_exec(move || {
            let flags = libc::fcntl(descriptor, libc::F_GETFD);
            if flags < 0 || libc::fcntl(descriptor, libc::F_SETFD, flags & !libc::FD_CLOEXEC) < 0 {
                return Err(std::io::Error::last_os_error());
            }
            Ok(())
        });
    }
}

pub fn prepare(
    bundle: &Path,
    prepared: &Path,
    cache: &Path,
    toolchain: &Toolchain,
) -> Result<PathBuf> {
    let dlls = assemblies(prepared)?;
    let main = published_loader(&fs::read_to_string(prepared.join("main.js"))?)?;
    fs::create_dir_all(cache)?;
    let temporary = tempfile::Builder::new()
        .prefix(".inputs-")
        .tempdir_in(cache)?;
    let generated = temporary.path().join("candidate");
    write_candidate(
        &bundle.join("aot-src"),
        &generated,
        &dlls,
        toolchain.identity["target_runtime"].as_str().unwrap(),
    )?;
    fs::write(
        generated.join("global.json"),
        serde_json::to_vec(&sdk_selection(
            toolchain.identity["version"].as_str().unwrap(),
        ))?,
    )?;
    let inputs = files::files(&generated)?;
    let key = files::digest(&inputs, &toolchain.identity, &mut BTreeMap::new())?;
    let policy = inputs
        .iter()
        .filter(|(name, _)| name.as_str() != "global.json" && !name.contains('/'))
        .map(|(n, p)| (n.clone(), p.clone()))
        .collect();
    let workspace_key = files::digest(&policy, &toolchain.identity, &mut BTreeMap::new())?;
    let compilation = cache.join("aot-builds").join(&key);
    fs::create_dir_all(&compilation)?;
    eprintln!("Checking mixed AOT cache: {}", compilation.display());
    // Publication lock spans all later composition, so readers observe one success.
    let _publication_lock = files::lock(&compilation.join("build.lock"))?;
    if !compilation.join("candidate").exists() {
        fs::rename(&generated, compilation.join("candidate"))?;
    }
    if !compilation.join("publish.complete").is_file() {
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
        &files::files(prepared)?,
        &json!({"compilation": key, "loader": main}),
        &mut BTreeMap::new(),
    )?;
    let runtimes = cache.join("aot-runtimes");
    fs::create_dir_all(&runtimes)?;
    let destination = runtimes.join(&runtime_key);
    if destination.join(".ready").is_file() {
        return Ok(destination);
    }
    let temporary = tempfile::Builder::new()
        .prefix(".compose-")
        .tempdir_in(&runtimes)?;
    let root = temporary.path().join("runtime");
    files::copy_tree(prepared, &root, |name| {
        name == "_framework" || name.starts_with("_framework/")
    })?;
    files::copy_tree(&published, &root, |_| false)?;
    fs::write(root.join("main.js"), main)?;
    fs::write(root.join(".ready"), runtime_key)?;
    if destination.exists() {
        bail!(
            "Incomplete AOT destination already exists: {}",
            destination.display()
        );
    }
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
