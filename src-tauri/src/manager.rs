use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    ffi::OsStr,
    fs,
    io::Read,
    net::{IpAddr, Ipv4Addr, SocketAddr},
    path::{Path, PathBuf},
    process::Command,
    thread,
    time::{Duration, SystemTime, UNIX_EPOCH},
};
use tauri::path::BaseDirectory;
use tauri::Manager;
#[cfg(windows)]
use windows::Win32::{
    Foundation::CloseHandle,
    System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Module32FirstW, Module32NextW, Process32FirstW, Process32NextW,
        MODULEENTRY32W, PROCESSENTRY32W, TH32CS_SNAPMODULE, TH32CS_SNAPMODULE32,
        TH32CS_SNAPPROCESS,
    },
};
#[cfg(windows)]
use winreg::{enums::*, RegKey};

const DLL_NAMES: [&str; 3] = ["OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll"];
const INSTALL_MANIFEST: &str = ".g-opensteamtool-dlls.json";
const META_PREFIX: &str = "-- GOST-META: ";

pub type Result<T> = std::result::Result<T, String>;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct ManagerSettings {
    pub log_level: String,
    pub manifest_url: String,
    pub timeout_resolve_ms: u64,
    pub timeout_connect_ms: u64,
    pub timeout_send_ms: u64,
    pub timeout_recv_ms: u64,
    pub lua_paths: Vec<String>,
    pub pattern_mirror: String,
}

impl Default for ManagerSettings {
    fn default() -> Self {
        Self {
            log_level: "info".into(),
            manifest_url: "wudrm".into(),
            timeout_resolve_ms: 5000,
            timeout_connect_ms: 5000,
            timeout_send_ms: 10000,
            timeout_recv_ms: 10000,
            lua_paths: Vec::new(),
            pattern_mirror: String::new(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct AppIdEntry {
    pub appid: u32,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub unlock_flag: Option<u32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub depot_key: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct GameConfig {
    pub appid: u32,
    pub name: String,
    #[serde(default = "default_true")]
    pub enabled: bool,
    pub depot_key: Option<String>,
    pub access_token: Option<String>,
    pub manifest_gid: Option<String>,
    pub app_ticket_hex: Option<String>,
    pub e_ticket_hex: Option<String>,
    pub stat_steam_id: Option<String>,
    #[serde(default)]
    pub appid_entries: Vec<AppIdEntry>,
}

fn default_true() -> bool {
    true
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum DllState {
    Missing,
    Managed,
    Foreign,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum DllLoadState {
    Loaded,
    NotLoaded,
    SteamNotRunning,
    VerifyFailed,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct DllStatus {
    pub name: String,
    pub state: DllState,
    pub target_path: String,
    pub resource_hash: Option<String>,
    pub target_hash: Option<String>,
    pub hash_matched: bool,
    pub loaded_by_steam: bool,
    pub load_state: DllLoadState,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct ScanState {
    pub steam_dir: String,
    pub steam_valid: bool,
    pub steam_running: bool,
    pub steam_version: Option<String>,
    pub config_exists: bool,
    pub lua_count: usize,
    pub dlls: Vec<DllStatus>,
    pub dll_resources_ready: bool,
    pub missing_dll_resources: Vec<String>,
    pub log_files: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct AppMetadata {
    pub appid: u32,
    pub name: String,
    pub source: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct LogFile {
    pub name: String,
    pub content: String,
    pub size_bytes: u64,
    pub modified_time: Option<u64>,
    pub line_count: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct GitHubReleaseInfo {
    pub version: String,
    pub name: String,
    pub published_at: Option<String>,
    pub body: String,
    pub html_url: String,
    pub assets: Vec<GitHubReleaseAsset>,
    pub dns_optimized: bool,
    pub resolved_hosts: Vec<ResolvedHost>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct GitHubReleaseAsset {
    pub name: String,
    pub browser_download_url: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct ResolvedHost {
    pub host: String,
    pub addresses: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CommandSpec {
    pub program: String,
    pub args: Vec<String>,
}

#[derive(Debug, Serialize, Deserialize)]
struct InstallManifest {
    dlls: BTreeMap<String, String>,
}

struct SteamModuleScan {
    steam_running: bool,
    verify_failed: bool,
    modules: Vec<(String, PathBuf)>,
}

pub fn validate_steam_dir<P: AsRef<Path>>(steam_dir: P) -> Result<PathBuf> {
    let path = steam_dir.as_ref();
    if !path.is_dir() {
        return Err("Steam directory does not exist".into());
    }
    let steam_exe = path.join("steam.exe");
    if !steam_exe.is_file() {
        return Err("Selected directory must contain steam.exe".into());
    }
    fs::canonicalize(path).map_err(|err| err.to_string())
}

pub fn detect_steam_dir() -> Result<Option<PathBuf>> {
    detect_steam_dir_from_candidates(steam_dir_candidates())
}

pub fn detect_steam_dir_from_candidates<I>(candidates: I) -> Result<Option<PathBuf>>
where
    I: IntoIterator<Item = PathBuf>,
{
    for candidate in candidates {
        if let Ok(path) = validate_steam_dir(&candidate) {
            return Ok(Some(path));
        }
    }
    Ok(None)
}

pub fn display_path<P: AsRef<Path>>(path: P) -> String {
    strip_verbatim_prefix(&path.as_ref().display().to_string())
}

pub fn strip_verbatim_prefix(path: &str) -> String {
    if let Some(rest) = path.strip_prefix(r"\\?\UNC\") {
        format!(r"\\{rest}")
    } else if let Some(rest) = path.strip_prefix(r"\\?\") {
        rest.to_string()
    } else {
        path.to_string()
    }
}

pub fn steam_dir_candidates() -> Vec<PathBuf> {
    let mut candidates = steam_registry_candidates();
    if let Ok(path) = std::env::var("ProgramFiles(x86)") {
        candidates.push(PathBuf::from(path).join("Steam"));
    }
    if let Ok(path) = std::env::var("ProgramFiles") {
        candidates.push(PathBuf::from(path).join("Steam"));
    }
    candidates.push(PathBuf::from("C:\\Program Files (x86)\\Steam"));

    dedupe_paths(candidates)
}

pub fn resource_dll_dir(app: &tauri::AppHandle) -> Result<PathBuf> {
    resolve_dll_resource_dir_from_candidates(dll_resource_candidates(app))
        .ok_or_else(|| "Missing bundled DLL resources".to_string())
}

pub fn dll_resource_candidates(app: &tauri::AppHandle) -> Vec<PathBuf> {
    let mut candidates = Vec::new();
    if let Ok(path) = app.path().resolve("dlls", BaseDirectory::Resource) {
        candidates.push(path);
    }
    if let Ok(dir) = app.path().resource_dir() {
        candidates.push(dir.join("dlls"));
    }
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            candidates.push(dir.join("dlls"));
            candidates.push(dir.join("resources").join("dlls"));
        }
    }
    if let Ok(cwd) = std::env::current_dir() {
        candidates.push(cwd.join("src-tauri").join("resources").join("dlls"));
        candidates.push(cwd.join("resources").join("dlls"));
        candidates.push(cwd.join("dlls"));
        if cwd.file_name().and_then(OsStr::to_str) == Some("src-tauri") {
            if let Some(parent) = cwd.parent() {
                candidates.push(parent.join("src-tauri").join("resources").join("dlls"));
                candidates.push(parent.join("dlls"));
            }
        }
    }
    dedupe_paths(candidates)
}

pub fn resolve_dll_resource_dir_from_candidates<I>(candidates: I) -> Option<PathBuf>
where
    I: IntoIterator<Item = PathBuf>,
{
    candidates
        .into_iter()
        .find(|candidate| missing_resources(Some(candidate)).is_empty())
}

pub fn load_settings_from_dir<P: AsRef<Path>>(steam_dir: P) -> Result<ManagerSettings> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let path = steam_dir.join("opensteamtool.toml");
    if !path.exists() {
        return Ok(ManagerSettings::default());
    }

    let text = fs::read_to_string(path).map_err(|err| err.to_string())?;
    let value: toml::Value = toml::from_str(&text).map_err(|err| err.to_string())?;
    let mut settings = ManagerSettings::default();

    if let Some(log) = value.get("log").and_then(|v| v.as_table()) {
        if let Some(level) = log.get("level").and_then(|v| v.as_str()) {
            settings.log_level = level.to_string();
        }
    }
    if let Some(manifest) = value.get("manifest").and_then(|v| v.as_table()) {
        if let Some(url) = manifest.get("url").and_then(|v| v.as_str()) {
            settings.manifest_url = url.to_string();
        }
        settings.timeout_resolve_ms =
            read_u64(manifest, "timeout_resolve_ms", settings.timeout_resolve_ms);
        settings.timeout_connect_ms =
            read_u64(manifest, "timeout_connect_ms", settings.timeout_connect_ms);
        settings.timeout_send_ms = read_u64(manifest, "timeout_send_ms", settings.timeout_send_ms);
        settings.timeout_recv_ms = read_u64(manifest, "timeout_recv_ms", settings.timeout_recv_ms);
    }
    if let Some(lua) = value.get("lua").and_then(|v| v.as_table()) {
        if let Some(paths) = lua.get("paths").and_then(|v| v.as_array()) {
            settings.lua_paths = paths
                .iter()
                .filter_map(|entry| entry.as_str().map(ToString::to_string))
                .collect();
        }
    }
    if let Some(pattern) = value.get("pattern").and_then(|v| v.as_table()) {
        if let Some(mirror) = pattern.get("mirror").and_then(|v| v.as_str()) {
            settings.pattern_mirror = mirror.to_string();
        }
    }

    Ok(settings)
}

pub fn save_settings_to_dir<P: AsRef<Path>>(
    steam_dir: P,
    settings: &ManagerSettings,
) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    validate_settings(settings)?;
    let lua_paths = settings
        .lua_paths
        .iter()
        .map(|path| format!("\"{}\"", escape_toml(path)))
        .collect::<Vec<_>>()
        .join(", ");
    let mut text = format!(
        "[log]\nlevel = \"{}\"\n\n[manifest]\nurl = \"{}\"\ntimeout_resolve_ms = {}\ntimeout_connect_ms = {}\ntimeout_send_ms = {}\ntimeout_recv_ms = {}\n\n[lua]\npaths = [{}]\n",
        escape_toml(&settings.log_level),
        escape_toml(&settings.manifest_url),
        settings.timeout_resolve_ms,
        settings.timeout_connect_ms,
        settings.timeout_send_ms,
        settings.timeout_recv_ms,
        lua_paths
    );
    if !settings.pattern_mirror.trim().is_empty() {
        text.push_str(&format!(
            "\n[pattern]\nmirror = \"{}\"\n",
            escape_toml(settings.pattern_mirror.trim())
        ));
    }
    fs::write(steam_dir.join("opensteamtool.toml"), text).map_err(|err| err.to_string())
}

pub fn render_game_lua(game: &GameConfig) -> Result<String> {
    validate_game(game)?;
    let meta = serde_json::to_string(game).map_err(|err| err.to_string())?;
    let mut lines = vec![
        format!("-- G-OpenSteamTool: {} {}", game.appid, game.name.trim()),
        format!("{META_PREFIX}{meta}"),
    ];

    for entry in normalized_appid_entries(game) {
        match (entry.unlock_flag, clean_opt(&entry.depot_key)) {
            (Some(flag), Some(key)) => {
                lines.push(format!("addappid({}, {}, \"{}\")", entry.appid, flag, key))
            }
            (Some(flag), None) => lines.push(format!("addappid({}, {})", entry.appid, flag)),
            (None, Some(key)) => lines.push(format!("addappid({}, 0, \"{}\")", entry.appid, key)),
            (None, None) => lines.push(format!("addappid({})", entry.appid)),
        }
    }
    if let Some(token) = clean_opt(&game.access_token) {
        lines.push(format!("addtoken({}, \"{}\")", game.appid, token));
    }
    if let Some(gid) = clean_opt(&game.manifest_gid) {
        lines.push(format!("setManifestid({}, \"{}\")", game.appid, gid));
    }
    if let Some(ticket) = clean_opt(&game.app_ticket_hex) {
        lines.push(format!("setAppTicket({}, \"{}\")", game.appid, ticket));
    }
    if let Some(ticket) = clean_opt(&game.e_ticket_hex) {
        lines.push(format!("setETicket({}, \"{}\")", game.appid, ticket));
    }
    if let Some(steam_id) = clean_opt(&game.stat_steam_id) {
        lines.push(format!("setStat({}, \"{}\")", game.appid, steam_id));
    }
    lines.push(String::new());
    Ok(lines.join("\n"))
}

pub fn upsert_game_in_dir<P: AsRef<Path>>(steam_dir: P, game: &GameConfig) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let lua_dir = lua_dir(&steam_dir);
    fs::create_dir_all(&lua_dir).map_err(|err| err.to_string())?;
    fs::write(
        lua_dir.join(game_file_name(game.appid, game.enabled)),
        render_game_lua(game)?,
    )
    .map_err(|err| err.to_string())
}

pub fn import_lua_file_from_path<P: AsRef<Path>, Q: AsRef<Path>>(
    steam_dir: P,
    source_path: Q,
) -> Result<GameConfig> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let source_path = source_path.as_ref();
    let file_name = source_path
        .file_name()
        .and_then(OsStr::to_str)
        .ok_or_else(|| "Invalid Lua file name".to_string())?;
    let text = fs::read_to_string(source_path).map_err(|err| err.to_string())?;
    let mut game =
        parse_game_lua(file_name, &text).ok_or_else(|| "无法从 Lua 文件识别 AppId".to_string())?;
    game.enabled = true;
    upsert_game_in_dir(&steam_dir, &game)?;
    Ok(game)
}

pub fn open_lua_dir_for_steam<P: AsRef<Path>>(steam_dir: P) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let lua_dir = lua_dir(&steam_dir);
    fs::create_dir_all(&lua_dir).map_err(|err| err.to_string())?;
    open_dir(&lua_dir)
}

pub fn list_games_from_dir<P: AsRef<Path>>(steam_dir: P) -> Result<Vec<GameConfig>> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let lua_dir = lua_dir(&steam_dir);
    if !lua_dir.is_dir() {
        return Ok(Vec::new());
    }
    let mut games = Vec::new();
    for entry in fs::read_dir(lua_dir).map_err(|err| err.to_string())? {
        let entry = entry.map_err(|err| err.to_string())?;
        let path = entry.path();
        let Some(name) = path.file_name().and_then(OsStr::to_str).map(str::to_string) else {
            continue;
        };
        if !is_managed_lua_name(&name) {
            continue;
        }
        let text = fs::read_to_string(path).map_err(|err| err.to_string())?;
        if let Some(mut game) = parse_game_lua(&name, &text) {
            game.enabled = is_enabled_lua_name(&name);
            games.push(game);
        }
    }
    games.sort_by_key(|game| game.appid);
    Ok(games)
}

pub fn delete_game_from_dir<P: AsRef<Path>>(steam_dir: P, appid: u32) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let lua_dir = lua_dir(&steam_dir);
    for enabled in [true, false] {
        let path = lua_dir.join(game_file_name(appid, enabled));
        if path.exists() {
            fs::remove_file(path).map_err(|err| err.to_string())?;
            return Ok(());
        }
    }
    Ok(())
}

pub fn set_game_enabled_in_dir<P: AsRef<Path>>(
    steam_dir: P,
    appid: u32,
    enabled: bool,
) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let lua_dir = lua_dir(&steam_dir);
    let source = lua_dir.join(game_file_name(appid, !enabled));
    let target = lua_dir.join(game_file_name(appid, enabled));
    if target.exists() {
        return Err(format!("目标 Lua 文件已存在：{}", target.display()));
    }
    if !source.exists() {
        return Err(format!("源 Lua 文件不存在：{}", source.display()));
    }
    fs::rename(source, target).map_err(|err| err.to_string())
}

pub fn scan_state_with_assets<P: AsRef<Path>, Q: AsRef<Path>>(
    steam_dir: P,
    assets_dir: Option<Q>,
) -> Result<ScanState> {
    let steam_dir = validate_steam_dir(&steam_dir)?;
    let asset_path = assets_dir.as_ref().map(|path| path.as_ref().to_path_buf());
    let missing_dll_resources = missing_resources(asset_path.as_deref());
    let module_scan = scan_steam_modules();
    let dlls = DLL_NAMES
        .iter()
        .map(|name| dll_status(&steam_dir, asset_path.as_deref(), name, &module_scan))
        .collect::<Result<Vec<_>>>()?;
    let log_files = list_log_names(&steam_dir);
    Ok(ScanState {
        steam_dir: display_path(&steam_dir),
        steam_valid: true,
        steam_running: module_scan.steam_running,
        steam_version: steam_version(&steam_dir),
        config_exists: steam_dir.join("opensteamtool.toml").exists(),
        lua_count: list_games_from_dir(&steam_dir)?.len(),
        dlls,
        dll_resources_ready: missing_dll_resources.is_empty(),
        missing_dll_resources,
        log_files,
    })
}

pub fn install_dlls_from_dir<P: AsRef<Path>, Q: AsRef<Path>>(
    steam_dir: P,
    assets_dir: Q,
) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let assets_dir = assets_dir.as_ref();
    let missing = missing_resources(Some(assets_dir));
    if !missing.is_empty() {
        return Err(format!(
            "Missing bundled DLL resources: {}",
            missing.join(", ")
        ));
    }

    let mut manifest = InstallManifest {
        dlls: BTreeMap::new(),
    };
    for name in DLL_NAMES {
        let src = assets_dir.join(name);
        let dst = steam_dir.join(name);
        fs::copy(&src, &dst).map_err(|err| err.to_string())?;
        manifest.dlls.insert(name.to_string(), file_hash(&dst)?);
    }
    write_manifest(&steam_dir, &manifest)
}

pub fn remove_dlls_from_dir<P: AsRef<Path>>(steam_dir: P) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let manifest = read_manifest(&steam_dir)?;
    for (name, expected_hash) in manifest.dlls {
        let path = steam_dir.join(&name);
        if path.exists() && file_hash(&path).ok() == Some(expected_hash) {
            fs::remove_file(path).map_err(|err| err.to_string())?;
        }
    }
    let manifest_path = steam_dir.join(INSTALL_MANIFEST);
    if manifest_path.exists() {
        fs::remove_file(manifest_path).map_err(|err| err.to_string())?;
    }
    Ok(())
}

pub fn fetch_app_metadata(appid: u32) -> Result<AppMetadata> {
    if appid == 0 {
        return Err("AppId must be greater than zero".into());
    }
    let url = format!("https://store.steampowered.com/api/appdetails?appids={appid}&filters=basic");
    let value: Value = reqwest::blocking::get(url)
        .map_err(|err| err.to_string())?
        .json()
        .map_err(|err| err.to_string())?;
    let name = value
        .get(appid.to_string())
        .and_then(|v| v.get("data"))
        .and_then(|v| v.get("name"))
        .and_then(Value::as_str)
        .unwrap_or("")
        .trim()
        .to_string();
    if name.is_empty() {
        return Err("Steam Store did not return an app name".into());
    }
    Ok(AppMetadata {
        appid,
        name,
        source: "store.steampowered.com".into(),
    })
}

pub fn github_domains_for_optimization() -> Vec<&'static str> {
    vec![
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
    ]
}

pub async fn resolve_github_domain_with_dot(host: &str) -> Result<Vec<String>> {
    if !github_domains_for_optimization().contains(&host) {
        return Err(format!("Unsupported GitHub host: {host}"));
    }

    let mut cloudflare = tauri::async_runtime::spawn({
        let host = host.to_string();
        async move { resolve_with_dot_provider(&host, true).await }
    });
    let mut google = tauri::async_runtime::spawn({
        let host = host.to_string();
        async move { resolve_with_dot_provider(&host, false).await }
    });

    tokio::select! {
        cloudflare_result = &mut cloudflare => {
            match cloudflare_result.map_err(|err| format!("Cloudflare DoT task failed: {err}"))? {
                Ok(addresses) => Ok(addresses),
                Err(first_error) => {
                    let google_result = google.await.map_err(|err| format!("Google DoT task failed: {err}"))?;
                    google_result.map_err(|err| format!("DoT resolve failed for {host}: {first_error}; {err}"))
                }
            }
        },
        google_result = &mut google => {
            match google_result.map_err(|err| format!("Google DoT task failed: {err}"))? {
                Ok(addresses) => Ok(addresses),
                Err(first_error) => {
                    let cloudflare_result = cloudflare.await.map_err(|err| format!("Cloudflare DoT task failed: {err}"))?;
                    cloudflare_result.map_err(|err| format!("DoT resolve failed for {host}: {first_error}; {err}"))
                }
            }
        },
    }
}

pub async fn check_github_release(dot_enabled: bool) -> Result<GitHubReleaseInfo> {
    let mut resolved_hosts = Vec::new();
    let mut builder = reqwest::Client::builder()
        .user_agent("G-OpenSteamTool/0.2.0")
        .timeout(Duration::from_secs(12));

    if dot_enabled {
        for host in github_domains_for_optimization() {
            let addresses = resolve_github_domain_with_dot(host).await?;
            let socket_addrs: Vec<SocketAddr> = addresses
                .iter()
                .filter_map(|address| format!("{address}:443").parse::<SocketAddr>().ok())
                .collect();
            if !socket_addrs.is_empty() {
                builder = builder.resolve_to_addrs(host, &socket_addrs);
                resolved_hosts.push(ResolvedHost {
                    host: host.to_string(),
                    addresses,
                });
            }
        }
    }

    let client = builder.build().map_err(|err| err.to_string())?;
    let value: Value = client
        .get("https://api.github.com/repos/G-Yoka/G-OpenSteamTool/releases/latest")
        .send()
        .await
        .map_err(|err| err.to_string())?
        .error_for_status()
        .map_err(|err| err.to_string())?
        .json()
        .await
        .map_err(|err| err.to_string())?;

    let version = value
        .get("tag_name")
        .and_then(Value::as_str)
        .unwrap_or("")
        .trim_start_matches('v')
        .to_string();
    if version.is_empty() {
        return Err("GitHub Releases did not return a tag name".into());
    }

    let assets = value
        .get("assets")
        .and_then(Value::as_array)
        .map(|items| {
            items
                .iter()
                .filter_map(|asset| {
                    Some(GitHubReleaseAsset {
                        name: asset.get("name")?.as_str()?.to_string(),
                        browser_download_url: asset
                            .get("browser_download_url")?
                            .as_str()?
                            .to_string(),
                    })
                })
                .collect()
        })
        .unwrap_or_default();

    Ok(GitHubReleaseInfo {
        version,
        name: value
            .get("name")
            .and_then(Value::as_str)
            .unwrap_or("GitHub Release")
            .to_string(),
        published_at: value
            .get("published_at")
            .and_then(Value::as_str)
            .map(ToString::to_string),
        body: value
            .get("body")
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string(),
        html_url: value
            .get("html_url")
            .and_then(Value::as_str)
            .unwrap_or("https://github.com/G-Yoka/G-OpenSteamTool/releases/latest")
            .to_string(),
        assets,
        dns_optimized: dot_enabled,
        resolved_hosts,
    })
}

async fn resolve_with_dot_provider(host: &str, cloudflare: bool) -> Result<Vec<String>> {
    use hickory_resolver::config::{ResolverConfig, ServerGroup};
    use hickory_resolver::net::runtime::TokioRuntimeProvider;
    use hickory_resolver::Resolver;

    let cloudflare_ips = [IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1))];
    let google_ips = [IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8))];
    let group = if cloudflare {
        ServerGroup {
            ips: &cloudflare_ips,
            server_name: "cloudflare-dns.com",
            path: "/dns-query",
        }
    } else {
        ServerGroup {
            ips: &google_ips,
            server_name: "dns.google",
            path: "/dns-query",
        }
    };
    let config = ResolverConfig::tls(&group);
    let resolver = Resolver::builder_with_config(config, TokioRuntimeProvider::default())
        .build()
        .map_err(|err| err.to_string())?;
    let lookup = resolver
        .lookup_ip(format!("{host}."))
        .await
        .map_err(|err| err.to_string())?;
    let addresses: Vec<String> = lookup.iter().map(|ip| ip.to_string()).collect();
    if addresses.is_empty() {
        Err("empty DNS answer".into())
    } else {
        Ok(addresses)
    }
}

pub fn read_logs_from_dir<P: AsRef<Path>>(steam_dir: P) -> Result<Vec<LogFile>> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    let log_dir = steam_dir.join("opensteamtool");
    if !log_dir.is_dir() {
        return Ok(Vec::new());
    }
    let mut logs = Vec::new();
    for entry in fs::read_dir(log_dir).map_err(|err| err.to_string())? {
        let entry = entry.map_err(|err| err.to_string())?;
        let path = entry.path();
        if path.extension().and_then(OsStr::to_str) != Some("log") {
            continue;
        }
        let name = path
            .file_name()
            .and_then(OsStr::to_str)
            .unwrap_or("unknown.log")
            .to_string();
        let metadata = fs::metadata(&path).map_err(|err| err.to_string())?;
        let size_bytes = metadata.len();
        let modified_time = metadata.modified().ok().and_then(system_time_secs);
        let mut content = fs::read_to_string(&path).unwrap_or_else(|_| String::new());
        if content.len() > 80_000 {
            content = content.split_off(content.len() - 80_000);
        }
        let line_count = content.lines().count();
        logs.push(LogFile {
            name,
            content,
            size_bytes,
            modified_time,
            line_count,
        });
    }
    logs.sort_by(|a, b| a.name.cmp(&b.name));
    Ok(logs)
}

fn system_time_secs(time: SystemTime) -> Option<u64> {
    time.duration_since(UNIX_EPOCH)
        .ok()
        .map(|duration| duration.as_secs())
}

pub fn close_steam() -> Result<()> {
    shutdown_steam(None)?;
    Ok(())
}

pub fn restart_steam<P: AsRef<Path>>(steam_dir: P) -> Result<()> {
    let steam_dir = validate_steam_dir(steam_dir)?;
    shutdown_steam(Some(&steam_dir))?;
    Command::new(steam_dir.join("steam.exe"))
        .current_dir(steam_dir)
        .spawn()
        .map_err(|err| err.to_string())?;
    Ok(())
}

fn read_u64(table: &toml::map::Map<String, toml::Value>, key: &str, default: u64) -> u64 {
    table
        .get(key)
        .and_then(|v| v.as_integer())
        .and_then(|v| u64::try_from(v).ok())
        .unwrap_or(default)
}

fn validate_settings(settings: &ManagerSettings) -> Result<()> {
    if !["trace", "debug", "info", "warn", "error"].contains(&settings.log_level.as_str()) {
        return Err("Invalid log level".into());
    }
    if !["steamrun", "wudrm"].contains(&settings.manifest_url.as_str()) {
        return Err("Invalid manifest source".into());
    }
    Ok(())
}

fn validate_game(game: &GameConfig) -> Result<()> {
    if game.appid == 0 {
        return Err("AppId must be greater than zero".into());
    }
    if let Some(key) = clean_opt(&game.depot_key) {
        if key.len() != 64 || !key.chars().all(|c| c.is_ascii_hexdigit()) {
            return Err("Depot key must be 64 hex characters".into());
        }
    }
    for entry in &game.appid_entries {
        if entry.appid == 0 {
            return Err("addappid entry AppId must be greater than zero".into());
        }
        if let Some(key) = clean_opt(&entry.depot_key) {
            if key.len() != 64 || !key.chars().all(|c| c.is_ascii_hexdigit()) {
                return Err("Depot key must be 64 hex characters".into());
            }
        }
    }
    for (label, value) in [
        ("access token", &game.access_token),
        ("manifest gid", &game.manifest_gid),
        ("stat steam id", &game.stat_steam_id),
    ] {
        if let Some(value) = clean_opt(value) {
            if !value.chars().all(|c| c.is_ascii_digit()) {
                return Err(format!("{label} must contain digits only"));
            }
        }
    }
    for (label, value) in [
        ("AppTicket", &game.app_ticket_hex),
        ("ETicket", &game.e_ticket_hex),
    ] {
        if let Some(value) = clean_opt(value) {
            if !value.chars().all(|c| c.is_ascii_hexdigit()) {
                return Err(format!("{label} must be hex"));
            }
        }
    }
    Ok(())
}

fn normalized_appid_entries(game: &GameConfig) -> Vec<AppIdEntry> {
    if !game.appid_entries.is_empty() {
        return game.appid_entries.clone();
    }
    vec![AppIdEntry {
        appid: game.appid,
        unlock_flag: clean_opt(&game.depot_key).map(|_| 0),
        depot_key: clean_opt(&game.depot_key),
    }]
}

fn parse_game_lua(file_name: &str, text: &str) -> Option<GameConfig> {
    if let Some(game) = text.lines().find_map(|line| {
        line.strip_prefix(META_PREFIX)
            .and_then(|json| serde_json::from_str(json).ok())
    }) {
        return Some(game);
    }
    parse_legacy_game_lua(file_name, text)
}

fn parse_legacy_game_lua(file_name: &str, text: &str) -> Option<GameConfig> {
    let entries = find_addappid_entries(text);
    let appid = appid_from_g_lua_name(file_name)
        .or_else(|| find_comment_value(text, "AppId").and_then(|value| value.parse().ok()))
        .or_else(|| entries.first().map(|entry| entry.appid))?;
    let name = find_comment_value(text, "备注")
        .or_else(|| find_comment_value(text, "Name"))
        .unwrap_or_else(|| format!("App {appid}"));
    let depot_key = entries
        .iter()
        .find(|entry| entry.appid == appid)
        .and_then(|entry| entry.depot_key.clone());

    Some(GameConfig {
        appid,
        name,
        enabled: true,
        depot_key,
        access_token: find_first_quoted_arg(text, "addtoken("),
        manifest_gid: find_first_quoted_arg(text, "setManifestid("),
        app_ticket_hex: find_first_quoted_arg(text, "setAppTicket("),
        e_ticket_hex: find_first_quoted_arg(text, "setETicket("),
        stat_steam_id: find_first_quoted_arg(text, "setStat("),
        appid_entries: entries,
    })
}

fn appid_from_g_lua_name(file_name: &str) -> Option<u32> {
    file_name
        .strip_prefix("G-")
        .and_then(|name| {
            name.strip_suffix(".lua")
                .or_else(|| name.strip_suffix(".lua.disabled"))
        })
        .and_then(|appid| appid.parse().ok())
}

fn find_comment_value(text: &str, key: &str) -> Option<String> {
    let prefix = format!("-- {key}:");
    text.lines()
        .find_map(|line| line.trim().strip_prefix(&prefix).map(str::trim))
        .filter(|value| !value.is_empty())
        .map(ToString::to_string)
}

fn find_addappid_entries(text: &str) -> Vec<AppIdEntry> {
    let mut entries = Vec::new();
    for args in find_call_args(text, "addappid(") {
        let parts = split_lua_args(&args);
        let Some(appid) = parts.first().and_then(|value| value.trim().parse().ok()) else {
            continue;
        };
        entries.push(AppIdEntry {
            appid,
            unlock_flag: parts.get(1).and_then(|value| value.trim().parse().ok()),
            depot_key: parts
                .get(2)
                .map(|value| value.trim())
                .and_then(unquote_lua_string),
        });
    }
    entries
}

fn find_call_args(text: &str, marker: &str) -> Vec<String> {
    text.split(marker)
        .skip(1)
        .filter_map(|segment| segment.split_once(')').map(|(args, _)| args.to_string()))
        .collect()
}

fn split_lua_args(args: &str) -> Vec<String> {
    let mut result = Vec::new();
    let mut current = String::new();
    let mut in_string = false;
    let mut escaped = false;
    for character in args.chars() {
        if escaped {
            current.push(character);
            escaped = false;
            continue;
        }
        if character == '\\' && in_string {
            current.push(character);
            escaped = true;
            continue;
        }
        if character == '"' {
            in_string = !in_string;
            current.push(character);
            continue;
        }
        if character == ',' && !in_string {
            result.push(current.trim().to_string());
            current.clear();
            continue;
        }
        current.push(character);
    }
    if !current.trim().is_empty() {
        result.push(current.trim().to_string());
    }
    result
}

fn unquote_lua_string(value: &str) -> Option<String> {
    value
        .strip_prefix('"')
        .and_then(|value| value.strip_suffix('"'))
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(ToString::to_string)
}

fn find_first_quoted_arg(text: &str, marker: &str) -> Option<String> {
    for segment in text.split(marker).skip(1) {
        let mut quoted = segment.split('"').skip(1);
        if let Some(value) = quoted
            .next()
            .map(str::trim)
            .filter(|value| !value.is_empty())
        {
            return Some(value.to_string());
        }
    }
    None
}

fn clean_opt(value: &Option<String>) -> Option<String> {
    value
        .as_ref()
        .map(|v| v.trim().to_string())
        .filter(|v| !v.is_empty())
}

fn lua_dir(steam_dir: &Path) -> PathBuf {
    steam_dir.join("config").join("lua")
}

fn game_file_name(appid: u32, enabled: bool) -> String {
    if enabled {
        format!("G-{appid}.lua")
    } else {
        format!("G-{appid}.lua.disabled")
    }
}

fn is_enabled_lua_name(name: &str) -> bool {
    name.starts_with("G-") && name.ends_with(".lua")
}

fn is_disabled_lua_name(name: &str) -> bool {
    name.starts_with("G-") && name.ends_with(".lua.disabled")
}

fn is_managed_lua_name(name: &str) -> bool {
    is_enabled_lua_name(name) || is_disabled_lua_name(name)
}

fn missing_resources(assets_dir: Option<&Path>) -> Vec<String> {
    DLL_NAMES
        .iter()
        .filter(|name| {
            assets_dir
                .map(|dir| !dir.join(name).is_file())
                .unwrap_or(true)
        })
        .map(|name| (*name).to_string())
        .collect()
}

fn dll_status(
    steam_dir: &Path,
    assets_dir: Option<&Path>,
    name: &str,
    module_scan: &SteamModuleScan,
) -> Result<DllStatus> {
    let target = steam_dir.join(name);
    let resource_hash = assets_dir
        .map(|asset_dir| asset_dir.join(name))
        .filter(|asset| asset.exists())
        .and_then(|asset| file_hash_hex(&asset).ok());
    let target_hash = target
        .exists()
        .then(|| file_hash_hex(&target))
        .transpose()?;
    let hash_matched = resource_hash.is_some() && resource_hash == target_hash;
    let state = if !target.exists() {
        DllState::Missing
    } else if let Some(asset_dir) = assets_dir {
        let asset = asset_dir.join(name);
        if asset.exists() && hash_matched {
            DllState::Managed
        } else {
            DllState::Foreign
        }
    } else {
        DllState::Foreign
    };
    let load_state = dll_load_state_from_modules(
        steam_dir,
        name,
        module_scan.steam_running,
        module_scan.verify_failed,
        &module_scan.modules,
    );
    Ok(DllStatus {
        name: name.to_string(),
        state,
        target_path: display_path(target),
        resource_hash,
        target_hash,
        hash_matched,
        loaded_by_steam: load_state == DllLoadState::Loaded,
        load_state,
    })
}

pub fn dll_load_state_from_modules(
    steam_dir: &Path,
    dll_name: &str,
    steam_running: bool,
    verify_failed: bool,
    modules: &[(String, PathBuf)],
) -> DllLoadState {
    if !steam_running {
        return DllLoadState::SteamNotRunning;
    }
    if verify_failed {
        return DllLoadState::VerifyFailed;
    }
    let loaded = modules.iter().any(|(module_name, module_path)| {
        module_name.eq_ignore_ascii_case(dll_name) && path_is_inside_dir(module_path, steam_dir)
    });
    if loaded {
        DllLoadState::Loaded
    } else {
        DllLoadState::NotLoaded
    }
}

fn path_is_inside_dir(path: &Path, dir: &Path) -> bool {
    let base = comparable_path(dir);
    let candidate = comparable_path(path);
    candidate == base || candidate.starts_with(&format!("{base}\\"))
}

fn comparable_path(path: &Path) -> String {
    display_path(path)
        .replace('/', "\\")
        .trim_end_matches('\\')
        .to_ascii_lowercase()
}

fn file_hash(path: &Path) -> Result<String> {
    let mut file = fs::File::open(path).map_err(|err| err.to_string())?;
    let mut hasher = Sha256::new();
    let mut buffer = [0_u8; 8192];
    loop {
        let read = file.read(&mut buffer).map_err(|err| err.to_string())?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(format!("{:x}", hasher.finalize()))
}

fn file_hash_hex(path: &Path) -> Result<String> {
    file_hash(path)
}

fn write_manifest(steam_dir: &Path, manifest: &InstallManifest) -> Result<()> {
    let text = serde_json::to_string_pretty(manifest).map_err(|err| err.to_string())?;
    fs::write(steam_dir.join(INSTALL_MANIFEST), text).map_err(|err| err.to_string())
}

fn read_manifest(steam_dir: &Path) -> Result<InstallManifest> {
    let path = steam_dir.join(INSTALL_MANIFEST);
    if !path.exists() {
        return Ok(InstallManifest {
            dlls: BTreeMap::new(),
        });
    }
    let text = fs::read_to_string(path).map_err(|err| err.to_string())?;
    serde_json::from_str(&text).map_err(|err| err.to_string())
}

fn list_log_names(steam_dir: &Path) -> Vec<String> {
    read_logs_from_dir(steam_dir)
        .map(|logs| logs.into_iter().map(|log| log.name).collect())
        .unwrap_or_default()
}

pub fn steam_shutdown_command_specs(steam_dir: Option<&Path>) -> Vec<CommandSpec> {
    let mut commands = Vec::new();
    if let Some(steam_dir) = steam_dir {
        commands.push(CommandSpec {
            program: display_path(steam_dir.join("steam.exe")),
            args: vec!["-shutdown".into()],
        });
    } else {
        commands.push(CommandSpec {
            program: "cmd".into(),
            args: vec![
                "/C".into(),
                "start".into(),
                "".into(),
                "steam://exit".into(),
            ],
        });
    }
    commands.push(force_steam_shutdown_command());
    commands
}

pub fn parse_steam_file_version_output(output: &str) -> Option<String> {
    output
        .lines()
        .map(str::trim)
        .find(|line| !line.is_empty())
        .map(ToString::to_string)
}

fn shutdown_steam(steam_dir: Option<&Path>) -> Result<()> {
    let commands = steam_shutdown_command_specs(steam_dir);
    if let Some(graceful) = commands.first() {
        let _ = run_command_spec(graceful);
    }
    wait_for_steam_exit(Duration::from_secs(4));
    if is_steam_running() {
        run_command_spec(&commands[1])?;
        wait_for_steam_exit(Duration::from_secs(4));
    }
    if is_steam_running() {
        return Err("Steam is still running after shutdown request".into());
    }
    Ok(())
}

fn wait_for_steam_exit(timeout: Duration) {
    let started = std::time::Instant::now();
    while started.elapsed() < timeout {
        if !is_steam_running() {
            break;
        }
        thread::sleep(Duration::from_millis(250));
    }
}

fn force_steam_shutdown_command() -> CommandSpec {
    CommandSpec {
        program: "taskkill".into(),
        args: vec!["/IM".into(), "steam.exe".into(), "/T".into(), "/F".into()],
    }
}

fn run_command_spec(spec: &CommandSpec) -> Result<()> {
    let status = Command::new(&spec.program)
        .args(&spec.args)
        .status()
        .map_err(|err| err.to_string())?;
    if status.success() {
        Ok(())
    } else {
        Err(format!(
            "Command failed: {} {}",
            spec.program,
            spec.args.join(" ")
        ))
    }
}

fn open_dir(path: &Path) -> Result<()> {
    #[cfg(windows)]
    {
        Command::new("explorer")
            .arg(display_path(path))
            .spawn()
            .map_err(|err| err.to_string())?;
        Ok(())
    }
    #[cfg(not(windows))]
    {
        Command::new("xdg-open")
            .arg(path)
            .spawn()
            .map_err(|err| err.to_string())?;
        Ok(())
    }
}

fn is_steam_running() -> bool {
    Command::new("powershell")
        .args([
            "-NoProfile",
            "-Command",
            "if (Get-Process -Name steam -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }",
        ])
        .status()
        .map(|status| status.success())
        .unwrap_or(false)
}

fn scan_steam_modules() -> SteamModuleScan {
    #[cfg(windows)]
    {
        scan_steam_modules_windows()
    }
    #[cfg(not(windows))]
    {
        SteamModuleScan {
            steam_running: false,
            verify_failed: false,
            modules: Vec::new(),
        }
    }
}

#[cfg(windows)]
fn scan_steam_modules_windows() -> SteamModuleScan {
    let mut scan = SteamModuleScan {
        steam_running: false,
        verify_failed: false,
        modules: Vec::new(),
    };

    let process_snapshot = match unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) } {
        Ok(handle) => handle,
        Err(_) => {
            scan.steam_running = is_steam_running();
            scan.verify_failed = scan.steam_running;
            return scan;
        }
    };

    let mut process = PROCESSENTRY32W {
        dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
        ..Default::default()
    };

    let mut has_process = unsafe { Process32FirstW(process_snapshot, &mut process).is_ok() };
    while has_process {
        let exe_name = wide_to_string(&process.szExeFile);
        if exe_name.eq_ignore_ascii_case("steam.exe") {
            scan.steam_running = true;
            match process_modules(process.th32ProcessID) {
                Ok(modules) => scan.modules.extend(modules),
                Err(_) => scan.verify_failed = true,
            }
        }
        has_process = unsafe { Process32NextW(process_snapshot, &mut process).is_ok() };
    }

    let _ = unsafe { CloseHandle(process_snapshot) };
    scan
}

#[cfg(windows)]
fn process_modules(process_id: u32) -> Result<Vec<(String, PathBuf)>> {
    let snapshot =
        unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, process_id) }
            .map_err(|err| err.to_string())?;

    let mut modules = Vec::new();
    let mut module = MODULEENTRY32W {
        dwSize: std::mem::size_of::<MODULEENTRY32W>() as u32,
        ..Default::default()
    };

    let mut has_module = unsafe { Module32FirstW(snapshot, &mut module).is_ok() };
    while has_module {
        modules.push((
            wide_to_string(&module.szModule),
            PathBuf::from(wide_to_string(&module.szExePath)),
        ));
        has_module = unsafe { Module32NextW(snapshot, &mut module).is_ok() };
    }

    let _ = unsafe { CloseHandle(snapshot) };
    Ok(modules)
}

#[cfg(windows)]
fn wide_to_string(value: &[u16]) -> String {
    let len = value
        .iter()
        .position(|character| *character == 0)
        .unwrap_or(value.len());
    String::from_utf16_lossy(&value[..len])
}

fn steam_version(steam_dir: &Path) -> Option<String> {
    steam_manifest_version(steam_dir)
        .or_else(|| steam_exe_file_version(&steam_dir.join("steam.exe")))
        .or_else(|| {
            let path = steam_dir.join("steam.cfg");
            fs::read_to_string(path).ok().and_then(|text| {
                text.lines()
                    .find(|line| line.to_ascii_lowercase().contains("version"))
                    .map(|line| line.trim().to_string())
            })
        })
}

fn steam_manifest_version(steam_dir: &Path) -> Option<String> {
    [
        steam_dir
            .join("package")
            .join("steam_client_win64.manifest"),
        steam_dir
            .join("package")
            .join("steam_client_win32.manifest"),
    ]
    .into_iter()
    .find_map(|path| {
        fs::read_to_string(path)
            .ok()
            .and_then(|text| parse_steam_manifest_version(&text))
    })
}

pub fn parse_steam_manifest_version(text: &str) -> Option<String> {
    text.lines().find_map(|line| {
        let trimmed = line.trim();
        if !trimmed.starts_with("\"version\"") {
            return None;
        }
        trimmed
            .split('"')
            .nth(3)
            .map(str::trim)
            .filter(|version| !version.is_empty() && version.chars().all(|c| c.is_ascii_digit()))
            .map(ToString::to_string)
    })
}

fn steam_exe_file_version(path: &Path) -> Option<String> {
    let literal_path = escape_powershell_single_quoted(&display_path(path));
    let script = format!(
        "(Get-Item -LiteralPath '{}').VersionInfo.FileVersion",
        literal_path
    );
    Command::new("powershell")
        .args(["-NoProfile", "-Command", &script])
        .output()
        .ok()
        .filter(|output| output.status.success())
        .and_then(|output| String::from_utf8(output.stdout).ok())
        .and_then(|version| parse_steam_file_version_output(&version))
}

fn escape_powershell_single_quoted(value: &str) -> String {
    value.replace('\'', "''")
}

fn escape_toml(value: &str) -> String {
    value.replace('\\', "\\\\").replace('"', "\\\"")
}

fn dedupe_paths(paths: Vec<PathBuf>) -> Vec<PathBuf> {
    let mut result = Vec::new();
    for path in paths {
        if !result.iter().any(|existing: &PathBuf| existing == &path) {
            result.push(path);
        }
    }
    result
}

#[cfg(windows)]
fn steam_registry_candidates() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    if let Ok(key) = RegKey::predef(HKEY_CURRENT_USER).open_subkey("Software\\Valve\\Steam") {
        if let Ok(path) = key.get_value::<String, _>("SteamPath") {
            candidates.push(PathBuf::from(normalize_steam_registry_path(path)));
        }
        if let Ok(exe) = key.get_value::<String, _>("SteamExe") {
            let exe = PathBuf::from(normalize_steam_registry_path(exe));
            if let Some(parent) = exe.parent() {
                candidates.push(parent.to_path_buf());
            }
        }
    }

    for root in [
        "SOFTWARE\\WOW6432Node\\Valve\\Steam",
        "SOFTWARE\\Valve\\Steam",
    ] {
        if let Ok(key) = RegKey::predef(HKEY_LOCAL_MACHINE).open_subkey(root) {
            if let Ok(path) = key.get_value::<String, _>("InstallPath") {
                candidates.push(PathBuf::from(normalize_steam_registry_path(path)));
            }
        }
    }

    candidates
}

#[cfg(not(windows))]
fn steam_registry_candidates() -> Vec<PathBuf> {
    Vec::new()
}

fn normalize_steam_registry_path(path: String) -> String {
    path.replace('/', "\\")
}
