use std::fs;

use g_opensteamtool::manager::{
    delete_game_from_dir, detect_steam_dir_from_candidates, github_domains_for_optimization,
    import_lua_file_from_path, install_dlls_from_dir, list_games_from_dir, load_settings_from_dir,
    parse_steam_manifest_version, read_logs_from_dir, remove_dlls_from_dir, render_game_lua,
    resolve_dll_resource_dir_from_candidates, save_settings_to_dir, scan_state_with_assets,
    set_game_enabled_in_dir, steam_shutdown_command_specs, strip_verbatim_prefix,
    upsert_game_in_dir, validate_steam_dir, DllLoadState, DllState, GameConfig, ManagerSettings,
};

fn make_steam_dir() -> tempfile::TempDir {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("steam.exe"), b"fake steam").unwrap();
    dir
}

#[test]
fn validate_steam_dir_requires_steam_exe() {
    let dir = tempfile::tempdir().unwrap();
    assert!(validate_steam_dir(dir.path()).is_err());

    fs::write(dir.path().join("steam.exe"), b"fake steam").unwrap();
    assert!(validate_steam_dir(dir.path()).is_ok());
}

#[test]
fn detect_steam_dir_prefers_first_valid_registry_candidate() {
    let invalid = tempfile::tempdir().unwrap();
    let valid = make_steam_dir();
    let fallback = make_steam_dir();

    let detected = detect_steam_dir_from_candidates([
        invalid.path().to_path_buf(),
        valid.path().to_path_buf(),
        fallback.path().to_path_buf(),
    ])
    .unwrap();

    assert_eq!(detected, Some(valid.path().canonicalize().unwrap()));
}

#[test]
fn display_paths_strip_windows_verbatim_prefix() {
    assert_eq!(
        strip_verbatim_prefix(r"\\?\D:\Launchers\Steam"),
        r"D:\Launchers\Steam"
    );
    assert_eq!(
        strip_verbatim_prefix(r"\\?\UNC\server\share\Steam"),
        r"\\server\share\Steam"
    );
}

#[test]
fn settings_round_trip_opensteamtool_toml() {
    let steam = make_steam_dir();
    let settings = ManagerSettings {
        log_level: "warn".into(),
        manifest_url: "steamrun".into(),
        timeout_resolve_ms: 1000,
        timeout_connect_ms: 2000,
        timeout_send_ms: 3000,
        timeout_recv_ms: 4000,
        lua_paths: vec!["D:/lua-extra".into()],
        pattern_mirror: "https://cdn.example/pattern".into(),
    };

    save_settings_to_dir(steam.path(), &settings).unwrap();
    let loaded = load_settings_from_dir(steam.path()).unwrap();

    assert_eq!(loaded, settings);
    let raw = fs::read_to_string(steam.path().join("opensteamtool.toml")).unwrap();
    assert!(raw.contains("[manifest]"));
    assert!(raw.contains("url = \"steamrun\""));
}

#[test]
fn render_game_lua_uses_g_prefixed_file_contract() {
    let game = sample_game();
    let lua = render_game_lua(&game).unwrap();

    assert!(lua.contains("-- G-OpenSteamTool: 753640 Outer Wilds"));
    assert!(lua.contains(
        "addappid(753640, 0, \"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\")"
    ));
    assert!(lua.contains("addtoken(753640, \"2764735786934684318\")"));
    assert!(lua.contains("setManifestid(753640, \"5656605350306673283\")"));
    assert!(lua.contains("setETicket(753640, \"abcdef\")"));
    assert!(lua.contains("setStat(753640, \"76561197960287930\")"));
}

#[test]
fn upsert_list_and_delete_game_only_touch_g_lua_files() {
    let steam = make_steam_dir();
    let lua_dir = steam.path().join("config").join("lua");
    fs::create_dir_all(&lua_dir).unwrap();
    fs::write(lua_dir.join("753640.lua"), "addappid(1)").unwrap();

    upsert_game_in_dir(steam.path(), &sample_game()).unwrap();

    assert!(lua_dir.join("G-753640.lua").exists());
    assert!(lua_dir.join("753640.lua").exists());
    let games = list_games_from_dir(steam.path()).unwrap();
    assert_eq!(games.len(), 1);
    assert_eq!(games[0].appid, 753640);
    assert!(games[0].enabled);

    delete_game_from_dir(steam.path(), 753640).unwrap();
    assert!(!lua_dir.join("G-753640.lua").exists());
    assert!(lua_dir.join("753640.lua").exists());
}

#[test]
fn list_games_reads_enabled_and_disabled_lua_files() {
    let steam = make_steam_dir();
    let lua_dir = steam.path().join("config").join("lua");
    fs::create_dir_all(&lua_dir).unwrap();
    fs::write(lua_dir.join("G-1962700.lua"), "addappid(1962700)\n").unwrap();
    fs::write(lua_dir.join("G-753640.lua.disabled"), "addappid(753640)\n").unwrap();

    let games = list_games_from_dir(steam.path()).unwrap();

    assert_eq!(games.len(), 2);
    assert_eq!(games[0].appid, 753640);
    assert!(!games[0].enabled);
    assert_eq!(games[1].appid, 1962700);
    assert!(games[1].enabled);
}

#[test]
fn set_game_enabled_renames_without_overwriting() {
    let steam = make_steam_dir();
    let lua_dir = steam.path().join("config").join("lua");
    fs::create_dir_all(&lua_dir).unwrap();
    fs::write(lua_dir.join("G-1962700.lua"), "addappid(1962700)\n").unwrap();

    set_game_enabled_in_dir(steam.path(), 1962700, false).unwrap();
    assert!(!lua_dir.join("G-1962700.lua").exists());
    assert!(lua_dir.join("G-1962700.lua.disabled").exists());

    set_game_enabled_in_dir(steam.path(), 1962700, true).unwrap();
    assert!(lua_dir.join("G-1962700.lua").exists());
    assert!(!lua_dir.join("G-1962700.lua.disabled").exists());

    fs::write(
        lua_dir.join("G-1962700.lua.disabled"),
        "addappid(1962700)\n",
    )
    .unwrap();
    let error = set_game_enabled_in_dir(steam.path(), 1962700, true).unwrap_err();
    assert!(error.contains("目标 Lua 文件已存在"));
}

#[test]
fn delete_game_removes_disabled_lua_when_enabled_file_is_missing() {
    let steam = make_steam_dir();
    let lua_dir = steam.path().join("config").join("lua");
    fs::create_dir_all(&lua_dir).unwrap();
    fs::write(
        lua_dir.join("G-1962700.lua.disabled"),
        "addappid(1962700)\n",
    )
    .unwrap();

    delete_game_from_dir(steam.path(), 1962700).unwrap();

    assert!(!lua_dir.join("G-1962700.lua.disabled").exists());
}

#[test]
fn upsert_disabled_game_keeps_disabled_suffix() {
    let steam = make_steam_dir();
    let mut game = sample_game();
    game.enabled = false;

    upsert_game_in_dir(steam.path(), &game).unwrap();

    let lua_dir = steam.path().join("config").join("lua");
    assert!(lua_dir.join("G-753640.lua.disabled").exists());
    assert!(!lua_dir.join("G-753640.lua").exists());
}

#[test]
fn list_games_accepts_legacy_g_lua_without_metadata() {
    let steam = make_steam_dir();
    let lua_dir = steam.path().join("config").join("lua");
    fs::create_dir_all(&lua_dir).unwrap();
    fs::write(
        lua_dir.join("G-1962700.lua"),
        r#"-- OpenSteamTool Manager
-- AppId: 1962700
-- This file is managed by OpenSteamTool Manager.
-- 备注: 深海迷航2

addappid(1962700)
addappid(1962701, 0, "96feb03fc707b975cf8271da4659fdb16c14c4b1a5c5bf8f94bd66e39edd4d96")
"#,
    )
    .unwrap();

    let games = list_games_from_dir(steam.path()).unwrap();

    assert_eq!(games.len(), 1);
    assert_eq!(games[0].appid, 1962700);
    assert_eq!(games[0].name, "深海迷航2");
    assert_eq!(games[0].depot_key.as_deref(), None);
    assert_eq!(games[0].appid_entries.len(), 2);
    assert_eq!(games[0].appid_entries[0].appid, 1962700);
    assert_eq!(games[0].appid_entries[0].depot_key.as_deref(), None);
    assert_eq!(games[0].appid_entries[1].appid, 1962701);
    assert_eq!(games[0].appid_entries[1].unlock_flag, Some(0));
    assert_eq!(
        games[0].appid_entries[1].depot_key.as_deref(),
        Some("96feb03fc707b975cf8271da4659fdb16c14c4b1a5c5bf8f94bd66e39edd4d96")
    );
}

#[test]
fn import_lua_file_parses_appid_and_writes_g_lua() {
    let steam = make_steam_dir();
    let source_dir = tempfile::tempdir().unwrap();
    let source = source_dir.path().join("legacy.lua");
    fs::write(&source, "addappid(1962700)\n").unwrap();

    let imported = import_lua_file_from_path(steam.path(), &source).unwrap();

    assert_eq!(imported.appid, 1962700);
    assert!(steam
        .path()
        .join("config")
        .join("lua")
        .join("G-1962700.lua")
        .exists());
}

#[test]
fn import_lua_file_preserves_multiple_addappid_entries() {
    let steam = make_steam_dir();
    let source_dir = tempfile::tempdir().unwrap();
    let source = source_dir.path().join("1962700.lua");
    fs::write(
        &source,
        r#"addappid(1962700)
addappid(1962701,0,"96feb03fc707b975cf8271da4659fdb16c14c4b1a5c5bf8f94bd66e39edd4d96")
"#,
    )
    .unwrap();

    let imported = import_lua_file_from_path(steam.path(), &source).unwrap();
    let output = fs::read_to_string(
        steam
            .path()
            .join("config")
            .join("lua")
            .join("G-1962700.lua"),
    )
    .unwrap();

    assert_eq!(imported.appid, 1962700);
    assert_eq!(imported.depot_key.as_deref(), None);
    assert!(output.contains("addappid(1962700)"));
    assert!(output.contains(
        "addappid(1962701, 0, \"96feb03fc707b975cf8271da4659fdb16c14c4b1a5c5bf8f94bd66e39edd4d96\")"
    ));
    assert!(!output.contains(
        "addappid(1962700, 0, \"96feb03fc707b975cf8271da4659fdb16c14c4b1a5c5bf8f94bd66e39edd4d96\")"
    ));
}

#[test]
fn import_lua_file_requires_appid() {
    let steam = make_steam_dir();
    let source_dir = tempfile::tempdir().unwrap();
    let source = source_dir.path().join("empty.lua");
    fs::write(&source, "-- no appid here\n").unwrap();

    let error = import_lua_file_from_path(steam.path(), &source).unwrap_err();

    assert!(error.contains("AppId"));
}

#[test]
fn scan_state_returns_display_paths_without_verbatim_prefix() {
    let steam = make_steam_dir();
    let state = scan_state_with_assets(steam.path(), Option::<&std::path::Path>::None).unwrap();

    assert!(!state.steam_dir.starts_with(r"\\?\"));
    assert!(state
        .dlls
        .iter()
        .all(|dll| !dll.target_path.starts_with(r"\\?\")));
}

#[test]
fn steam_shutdown_command_plan_tries_graceful_then_forceful() {
    let steam = make_steam_dir();
    let commands = steam_shutdown_command_specs(Some(steam.path()));

    assert_eq!(
        commands[0].program,
        steam.path().join("steam.exe").display().to_string()
    );
    assert_eq!(commands[0].args, vec!["-shutdown"]);
    assert_eq!(commands[1].program, "taskkill");
    assert_eq!(commands[1].args, vec!["/IM", "steam.exe", "/T", "/F"]);
}

#[test]
fn steam_manifest_version_reads_top_level_build_number() {
    assert_eq!(
        parse_steam_manifest_version("\"win64\"\n{\n    \"version\"    \"1779918128\"\n}\n"),
        Some("1779918128".into())
    );
    assert_eq!(parse_steam_manifest_version("\"win64\"\n{\n}\n"), None);
}

#[test]
fn scan_state_prefers_win64_manifest_version_then_win32() {
    let steam = make_steam_dir();
    let package = steam.path().join("package");
    fs::create_dir_all(&package).unwrap();
    fs::write(
        package.join("steam_client_win32.manifest"),
        "\"win32\"\n{\n    \"version\"    \"1769731672\"\n}\n",
    )
    .unwrap();
    fs::write(
        package.join("steam_client_win64.manifest"),
        "\"win64\"\n{\n    \"version\"    \"1779918128\"\n}\n",
    )
    .unwrap();

    let state = scan_state_with_assets(steam.path(), Option::<&std::path::Path>::None).unwrap();
    assert_eq!(state.steam_version.as_deref(), Some("1779918128"));

    fs::remove_file(package.join("steam_client_win64.manifest")).unwrap();
    let fallback = scan_state_with_assets(steam.path(), Option::<&std::path::Path>::None).unwrap();
    assert_eq!(fallback.steam_version.as_deref(), Some("1769731672"));
}

#[test]
fn scan_install_and_remove_dlls_from_asset_dir() {
    let steam = make_steam_dir();
    let assets = tempfile::tempdir().unwrap();
    for name in ["OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll"] {
        fs::write(assets.path().join(name), format!("asset:{name}")).unwrap();
    }

    let before = scan_state_with_assets(steam.path(), Some(assets.path())).unwrap();
    assert!(before.dlls.iter().all(|dll| dll.state == DllState::Missing));
    assert!(before.dlls.iter().all(|dll| dll.resource_hash.is_some()));
    assert!(before
        .dlls
        .iter()
        .all(|dll| dll.resource_hash.as_deref().is_some_and(is_sha256_hex)));
    assert!(before.dlls.iter().all(|dll| dll.target_hash.is_none()));
    assert!(before.dlls.iter().all(|dll| !dll.hash_matched));
    assert!(before.dll_resources_ready);

    install_dlls_from_dir(steam.path(), assets.path()).unwrap();
    let after = scan_state_with_assets(steam.path(), Some(assets.path())).unwrap();
    assert!(after.dlls.iter().all(|dll| dll.state == DllState::Managed));
    assert!(after.dlls.iter().all(|dll| dll.resource_hash.is_some()));
    assert!(after.dlls.iter().all(|dll| dll.target_hash.is_some()));
    assert!(after
        .dlls
        .iter()
        .all(|dll| dll.target_hash.as_deref().is_some_and(is_sha256_hex)));
    assert!(after.dlls.iter().all(|dll| dll.hash_matched));

    remove_dlls_from_dir(steam.path()).unwrap();
    let removed = scan_state_with_assets(steam.path(), Some(assets.path())).unwrap();
    assert!(removed
        .dlls
        .iter()
        .all(|dll| dll.state == DllState::Missing));
}

#[test]
fn scan_reports_sha256_mismatch_for_foreign_dlls() {
    let steam = make_steam_dir();
    let assets = tempfile::tempdir().unwrap();
    for name in ["OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll"] {
        fs::write(assets.path().join(name), format!("asset:{name}")).unwrap();
        fs::write(steam.path().join(name), format!("foreign:{name}")).unwrap();
    }

    let state = scan_state_with_assets(steam.path(), Some(assets.path())).unwrap();

    assert!(state.dlls.iter().all(|dll| dll.state == DllState::Foreign));
    assert!(state.dlls.iter().all(|dll| !dll.hash_matched));
    assert!(state
        .dlls
        .iter()
        .all(|dll| dll.resource_hash != dll.target_hash));
    assert!(state
        .dlls
        .iter()
        .all(|dll| dll.resource_hash.as_deref().is_some_and(is_sha256_hex)));
    assert!(state
        .dlls
        .iter()
        .all(|dll| dll.target_hash.as_deref().is_some_and(is_sha256_hex)));
}

#[test]
fn scan_reports_missing_dll_resources() {
    let steam = make_steam_dir();
    let assets = tempfile::tempdir().unwrap();

    let state = scan_state_with_assets(steam.path(), Some(assets.path())).unwrap();

    assert!(!state.dll_resources_ready);
    assert_eq!(state.missing_dll_resources.len(), 3);
    assert!(state.dlls.iter().all(|dll| dll.resource_hash.is_none()));
}

#[test]
fn read_logs_returns_metadata_for_log_files() {
    let steam = make_steam_dir();
    let log_dir = steam.path().join("opensteamtool");
    fs::create_dir_all(&log_dir).unwrap();
    fs::write(
        log_dir.join("opensteamtool.log"),
        "2026-05-31 INFO ready\nWARN slow\n",
    )
    .unwrap();
    fs::write(log_dir.join("ignore.txt"), "nope").unwrap();

    let logs = read_logs_from_dir(steam.path()).unwrap();

    assert_eq!(logs.len(), 1);
    assert_eq!(logs[0].name, "opensteamtool.log");
    assert_eq!(logs[0].line_count, 2);
    assert!(logs[0].size_bytes > 0);
    assert!(logs[0].modified_time.is_some());
}

#[test]
fn github_dns_optimization_hosts_cover_release_domains() {
    let hosts = github_domains_for_optimization();

    assert!(hosts.contains(&"github.com"));
    assert!(hosts.contains(&"api.github.com"));
    assert!(hosts.contains(&"objects.githubusercontent.com"));
    assert!(hosts.contains(&"github-releases.githubusercontent.com"));
}

#[test]
fn dll_load_state_requires_steam_running_and_matching_steam_path() {
    let steam = make_steam_dir();
    let loaded_path = steam.path().join("dwmapi.dll");
    let outside = tempfile::tempdir().unwrap().path().join("dwmapi.dll");
    let modules = vec![
        ("dwmapi.dll".to_string(), outside),
        (
            "OpenSteamTool.dll".to_string(),
            steam.path().join("OpenSteamTool.dll"),
        ),
        ("dwmapi.dll".to_string(), loaded_path),
    ];

    assert_eq!(
        g_opensteamtool::manager::dll_load_state_from_modules(
            steam.path(),
            "dwmapi.dll",
            true,
            false,
            &modules,
        ),
        DllLoadState::Loaded
    );
    assert_eq!(
        g_opensteamtool::manager::dll_load_state_from_modules(
            steam.path(),
            "xinput1_4.dll",
            true,
            false,
            &modules,
        ),
        DllLoadState::NotLoaded
    );
    assert_eq!(
        g_opensteamtool::manager::dll_load_state_from_modules(
            steam.path(),
            "dwmapi.dll",
            false,
            false,
            &modules,
        ),
        DllLoadState::SteamNotRunning
    );
    assert_eq!(
        g_opensteamtool::manager::dll_load_state_from_modules(
            steam.path(),
            "dwmapi.dll",
            true,
            true,
            &modules,
        ),
        DllLoadState::VerifyFailed
    );
}

#[test]
fn dll_resource_resolution_accepts_later_development_candidate() {
    let missing = tempfile::tempdir().unwrap();
    let dev_assets = tempfile::tempdir().unwrap();
    for name in ["OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll"] {
        fs::write(dev_assets.path().join(name), format!("asset:{name}")).unwrap();
    }

    let resolved = resolve_dll_resource_dir_from_candidates([
        missing.path().to_path_buf(),
        dev_assets.path().to_path_buf(),
    ]);

    assert_eq!(resolved, Some(dev_assets.path().to_path_buf()));
}

fn sample_game() -> GameConfig {
    GameConfig {
        appid: 753640,
        name: "Outer Wilds".into(),
        enabled: true,
        depot_key: Some("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef".into()),
        access_token: Some("2764735786934684318".into()),
        manifest_gid: Some("5656605350306673283".into()),
        app_ticket_hex: None,
        e_ticket_hex: Some("abcdef".into()),
        stat_steam_id: Some("76561197960287930".into()),
        appid_entries: Vec::new(),
    }
}

fn is_sha256_hex(value: &str) -> bool {
    value.len() == 64 && value.chars().all(|ch| ch.is_ascii_hexdigit())
}
