export type DllState = "Missing" | "Managed" | "Foreign";
export type DllLoadState = "Loaded" | "NotLoaded" | "SteamNotRunning" | "VerifyFailed";

export type DllStatus = {
  name: string;
  state: DllState;
  target_path: string;
  resource_hash?: string | null;
  target_hash?: string | null;
  hash_matched: boolean;
  loaded_by_steam: boolean;
  load_state: DllLoadState;
};

export type ScanState = {
  steam_dir: string;
  steam_valid: boolean;
  steam_running: boolean;
  steam_version: string | null;
  config_exists: boolean;
  lua_count: number;
  dlls: DllStatus[];
  dll_resources_ready: boolean;
  missing_dll_resources: string[];
  log_files: string[];
};

export type ManagerSettings = {
  log_level: string;
  manifest_url: string;
  timeout_resolve_ms: number;
  timeout_connect_ms: number;
  timeout_send_ms: number;
  timeout_recv_ms: number;
  lua_paths: string[];
  pattern_mirror: string;
};

export type GameConfig = {
  appid: number;
  name: string;
  enabled: boolean;
  depot_key?: string | null;
  access_token?: string | null;
  manifest_gid?: string | null;
  app_ticket_hex?: string | null;
  e_ticket_hex?: string | null;
  stat_steam_id?: string | null;
  appid_entries?: AppIdEntry[];
};

export type AppIdEntry = {
  appid: number;
  unlock_flag?: number | null;
  depot_key?: string | null;
};

export type AppMetadata = {
  appid: number;
  name: string;
  source: string;
};

export type LogFile = {
  name: string;
  content: string;
  size_bytes: number;
  modified_time?: number | null;
  line_count: number;
};

export type GitHubReleaseAsset = {
  name: string;
  browser_download_url: string;
};

export type ResolvedHost = {
  host: string;
  addresses: string[];
};

export type GitHubReleaseInfo = {
  version: string;
  name: string;
  published_at?: string | null;
  body: string;
  html_url: string;
  assets: GitHubReleaseAsset[];
  dns_optimized: boolean;
  resolved_hosts: ResolvedHost[];
};
