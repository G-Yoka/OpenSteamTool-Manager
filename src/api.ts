import { invoke } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";
import type { AppMetadata, GameConfig, GitHubReleaseInfo, LogFile, ManagerSettings, ScanState } from "./types";

const hasTauri = () => "__TAURI_INTERNALS__" in window;

const previewDll = (steamDir: string, name: string): ScanState["dlls"][number] => ({
  name,
  state: "Missing",
  target_path: `${steamDir}\\${name}`,
  resource_hash: null,
  target_hash: null,
  hash_matched: false,
  loaded_by_steam: false,
  load_state: "SteamNotRunning",
});

const previewState = (steamDir: string): ScanState => ({
  steam_dir: steamDir,
  steam_valid: Boolean(steamDir),
  steam_running: false,
  steam_version: null,
  config_exists: false,
  lua_count: 0,
  dlls: ["OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll"].map((name) => previewDll(steamDir, name)),
  dll_resources_ready: false,
  missing_dll_resources: ["OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll"],
  log_files: [],
});

const previewSettings: ManagerSettings = {
  log_level: "info",
  manifest_url: "wudrm",
  timeout_resolve_ms: 5000,
  timeout_connect_ms: 5000,
  timeout_send_ms: 10000,
  timeout_recv_ms: 10000,
  lua_paths: [],
  pattern_mirror: "",
};

export async function detectSteamDir(): Promise<string | null> {
  if (!hasTauri()) return null;
  return invoke<string | null>("detect_steam_dir");
}

export async function pickSteamDir(): Promise<string | null> {
  if (!hasTauri()) return null;
  const picked = await open({ directory: true, multiple: false, title: "选择 Steam 根目录" });
  return typeof picked === "string" ? picked : null;
}

export async function scanState(steamDir: string): Promise<ScanState> {
  if (!hasTauri()) return previewState(steamDir);
  return invoke<ScanState>("scan_state", { steamDir });
}

export async function installDlls(steamDir: string): Promise<void> {
  if (!hasTauri()) return;
  return invoke("install_dlls", { steamDir });
}

export async function removeDlls(steamDir: string): Promise<void> {
  if (!hasTauri()) return;
  return invoke("remove_dlls", { steamDir });
}

export async function loadSettings(steamDir: string): Promise<ManagerSettings> {
  if (!hasTauri()) return previewSettings;
  return invoke<ManagerSettings>("load_settings", { steamDir });
}

export async function saveSettings(steamDir: string, settings: ManagerSettings): Promise<void> {
  if (!hasTauri()) return;
  return invoke("save_settings", { steamDir, settings });
}

export async function listGames(steamDir: string): Promise<GameConfig[]> {
  if (!hasTauri()) return [];
  return invoke<GameConfig[]>("list_games", { steamDir });
}

export async function importLuaFile(steamDir: string): Promise<GameConfig | null> {
  if (!hasTauri()) return null;
  return invoke<GameConfig | null>("import_lua_file", { steamDir });
}

export async function openLuaDir(steamDir: string): Promise<void> {
  if (!hasTauri()) return;
  return invoke("open_lua_dir", { steamDir });
}

export async function upsertGame(steamDir: string, game: GameConfig): Promise<void> {
  if (!hasTauri()) return;
  return invoke("upsert_game", { steamDir, game });
}

export async function deleteGame(steamDir: string, appid: number): Promise<void> {
  if (!hasTauri()) return;
  return invoke("delete_game", { steamDir, appid });
}

export async function setGameEnabled(steamDir: string, appid: number, enabled: boolean): Promise<void> {
  if (!hasTauri()) return;
  return invoke("set_game_enabled", { steamDir, appid, enabled });
}

export async function fetchAppMetadata(appid: number): Promise<AppMetadata> {
  if (!hasTauri()) return { appid, name: `App ${appid}`, source: "preview" };
  return invoke<AppMetadata>("fetch_app_metadata", { appid });
}

export async function readLogs(steamDir: string): Promise<LogFile[]> {
  if (!hasTauri()) return [];
  return invoke<LogFile[]>("read_logs", { steamDir });
}

export async function checkGithubRelease(dotEnabled: boolean): Promise<GitHubReleaseInfo> {
  if (!hasTauri()) {
    return {
      version: "0.2.0",
      name: "Preview",
      published_at: null,
      body: "Tauri 应用内可检查 GitHub Releases。",
      html_url: "https://github.com/G-Yoka/G-OpenSteamTool/releases/latest",
      assets: [],
      dns_optimized: dotEnabled,
      resolved_hosts: [],
    };
  }
  return invoke<GitHubReleaseInfo>("check_github_release", { dotEnabled });
}

export async function resolveGithubDomainWithDot(host: string): Promise<string[]> {
  if (!hasTauri()) return [];
  return invoke<string[]>("resolve_github_domain_with_dot", { host });
}

export async function closeSteam(): Promise<void> {
  if (!hasTauri()) return;
  return invoke("close_steam");
}

export async function restartSteam(steamDir: string): Promise<void> {
  if (!hasTauri()) return;
  return invoke("restart_steam", { steamDir });
}

export async function minimizeWindow(): Promise<void> {
  if (!hasTauri()) return;
  return invoke("minimize_window");
}

export async function toggleMaximizeWindow(): Promise<void> {
  if (!hasTauri()) return;
  return invoke("toggle_maximize_window");
}

export async function closeWindow(): Promise<void> {
  if (!hasTauri()) return;
  return invoke("close_window");
}
