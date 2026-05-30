import {
  Activity,
  BookOpen,
  Box,
  CheckCircle2,
  ChevronRight,
  Code2,
  Download,
  Folder,
  Gauge,
  Info,
  ListChecks,
  PlayCircle,
  RefreshCcw,
  RotateCw,
  Save,
  Settings,
  ShieldAlert,
  Square,
  X,
  Trash2,
  XCircle,
} from "lucide-react";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { check, type Update } from "@tauri-apps/plugin-updater";
import { relaunch } from "@tauri-apps/plugin-process";
import { useEffect, useMemo, useRef, useState } from "react";
import type { MouseEvent } from "react";
import {
  closeSteam,
  closeWindow,
  checkGithubRelease,
  deleteGame,
  detectSteamDir,
  fetchAppMetadata,
  importLuaFile,
  installDlls,
  listGames,
  loadSettings,
  minimizeWindow,
  openLuaDir,
  pickSteamDir,
  readLogs,
  removeDlls,
  restartSteam,
  saveSettings,
  scanState,
  setGameEnabled,
  toggleMaximizeWindow,
  upsertGame,
} from "./api";
import yokaStarMoonFlower from "./assets/yoka-star-moon-flower.png";
import type { AppIdEntry, DllStatus, GameConfig, GitHubReleaseInfo, LogFile, ManagerSettings, ScanState } from "./types";

type Tab = "overview" | "lua" | "dll" | "settings" | "logs" | "about";
type Busy = "idle" | "loading" | "saving" | "working";
type ConsoleKind = "idle" | "working" | "success" | "error";
type ConsoleState = {
  kind: ConsoleKind;
  text: string;
  time: string;
};

const tabs: Array<{ id: Tab; label: string; icon: typeof Gauge }> = [
  { id: "overview", label: "概览", icon: Gauge },
  { id: "lua", label: "Lua", icon: Code2 },
  { id: "dll", label: "DLL", icon: Box },
  { id: "settings", label: "设置", icon: Settings },
  { id: "logs", label: "日志", icon: ListChecks },
  { id: "about", label: "关于", icon: Info },
];

const defaultSettings: ManagerSettings = {
  log_level: "info",
  manifest_url: "wudrm",
  timeout_resolve_ms: 5000,
  timeout_connect_ms: 5000,
  timeout_send_ms: 10000,
  timeout_recv_ms: 10000,
  lua_paths: [],
  pattern_mirror: "",
};

const CANVAS_WIDTH = 1260;
const CANVAS_HEIGHT = 800;
const APP_VERSION = "0.2.0";
const PROJECT_URL = "https://github.com/G-Yoka/G-OpenSteamTool";
const DNS_SETTING_KEY = "gost.githubDnsOptimization";
const hasTauriRuntime = () => typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;

const initialConsole: ConsoleState = {
  kind: "idle",
  text: "状态控制台待命",
  time: "--:--:--",
};

const emptyGame: GameConfig = {
  appid: 0,
  name: "",
  enabled: true,
  depot_key: "",
  access_token: "",
  manifest_gid: "",
  app_ticket_hex: "",
  e_ticket_hex: "",
  stat_steam_id: "",
  appid_entries: [],
};

function App() {
  const canvasScale = useCanvasScale();
  const [tab, setTab] = useState<Tab>("overview");
  const [steamDir, setSteamDir] = useState("");
  const [state, setState] = useState<ScanState | null>(null);
  const [settings, setSettings] = useState<ManagerSettings>(defaultSettings);
  const [games, setGames] = useState<GameConfig[]>([]);
  const [logs, setLogs] = useState<LogFile[]>([]);
  const [selectedLog, setSelectedLog] = useState("");
  const [gameForm, setGameForm] = useState<GameConfig>(emptyGame);
  const [luaPathsText, setLuaPathsText] = useState("");
  const [githubDnsOptimization, setGithubDnsOptimization] = useState(() => window.localStorage.getItem(DNS_SETTING_KEY) === "1");
  const [consoleState, setConsoleState] = useState<ConsoleState>(initialConsole);
  const [busy, setBusy] = useState<Busy>("idle");

  const consoleTimer = useRef<number | null>(null);
  const dllSummary = useMemo(() => summarizeDllOverview(state?.dlls ?? []), [state]);
  const activeLog = logs.find((log) => log.name === selectedLog) ?? logs[0];

  useEffect(() => {
    void boot();
  }, []);

  useEffect(() => {
    if (!steamDir || busy !== "idle") return;
    const timer = window.setInterval(() => {
      void refreshScanState(steamDir);
    }, 5000);
    return () => window.clearInterval(timer);
  }, [steamDir, busy]);

  useEffect(() => {
    return () => {
      if (consoleTimer.current) {
        window.clearTimeout(consoleTimer.current);
      }
    };
  }, []);

  useEffect(() => {
    window.localStorage.setItem(DNS_SETTING_KEY, githubDnsOptimization ? "1" : "0");
  }, [githubDnsOptimization]);

  async function boot() {
    setBusy("loading");
    try {
      const detected = await detectSteamDir();
      if (detected) {
        setSteamDir(detected);
        await refreshAll(detected);
      }
    } catch (err) {
      pushConsole("error", String(err));
    } finally {
      setBusy("idle");
    }
  }

  async function refreshAll(dir = steamDir) {
    if (!dir) return;
    setBusy("loading");
    try {
      const [nextState, nextSettings, nextGames, nextLogs] = await Promise.all([
        scanState(dir),
        loadSettings(dir),
        listGames(dir),
        readLogs(dir),
      ]);
      setState(nextState);
      setSettings(nextSettings);
      setLuaPathsText(nextSettings.lua_paths.join("\n"));
      setGames(nextGames);
      setLogs(nextLogs);
      setSelectedLog((current) => current || nextLogs[0]?.name || "");
    } catch (err) {
      pushConsole("error", String(err));
    } finally {
      setBusy("idle");
    }
  }

  async function refreshScanState(dir = steamDir) {
    if (!dir) return;
    try {
      const nextState = await scanState(dir);
      setState(nextState);
    } catch {
      // Keep polling quiet; explicit refresh/actions still report errors in the console.
    }
  }

  async function chooseSteamDir() {
    const picked = await pickSteamDir();
    if (!picked) return;
    setSteamDir(picked);
    await refreshAll(picked);
  }

  function pushConsole(kind: ConsoleKind, text: string) {
    if (consoleTimer.current) {
      window.clearTimeout(consoleTimer.current);
      consoleTimer.current = null;
    }
    setConsoleState({ kind, text, time: currentTime() });
    if (kind !== "working") {
      consoleTimer.current = window.setTimeout(() => {
        setConsoleState(initialConsole);
        consoleTimer.current = null;
      }, 5000);
    }
  }

  function clearConsole() {
    if (consoleTimer.current) {
      window.clearTimeout(consoleTimer.current);
      consoleTimer.current = null;
    }
    setConsoleState(initialConsole);
  }

  async function runAction(action: () => Promise<void>, success: string, working = "正在执行操作...") {
    setBusy("working");
    pushConsole("working", working);
    try {
      await action();
      pushConsole("success", success);
      await refreshAll();
    } catch (err) {
      pushConsole("error", String(err));
    } finally {
      setBusy("idle");
    }
  }

  async function saveCurrentSettings() {
    const next = {
      ...settings,
      lua_paths: luaPathsText
        .split(/\r?\n/)
        .map((item) => item.trim())
        .filter(Boolean),
    };
    await runAction(async () => saveSettings(steamDir, next), "设置已保存到 opensteamtool.toml");
  }

  async function initializeApp() {
    await runAction(
      async () => {
        await installDlls(steamDir);
        await saveSettings(steamDir, defaultSettings);
      },
      "初始化完成：DLL 已安装，默认 TOML 已保存",
      "正在初始化：安装 DLL 并保存默认 TOML..."
    );
  }

  async function saveGame() {
    const normalized = normalizeGameForm(gameForm);
    await runAction(async () => upsertGame(steamDir, normalized), `已保存 G-${normalized.appid}.lua`);
    setGameForm(emptyGame);
  }

  function editGame(game: GameConfig) {
    setGameForm(normalizeGameForm(game));
  }

  async function importLua() {
    setBusy("working");
    pushConsole("working", "正在导入 Lua...");
    try {
      const game = await importLuaFile(steamDir);
      if (!game) {
        pushConsole("idle", "已取消导入 Lua");
        return;
      }
      pushConsole("success", `已导入 G-${game.appid}.lua`);
      await refreshAll();
    } catch (err) {
      pushConsole("error", String(err));
    } finally {
      setBusy("idle");
    }
  }

  async function autoFetch() {
    if (!gameForm.appid) {
      pushConsole("error", "请先输入 AppId");
      return;
    }
    setBusy("loading");
    pushConsole("working", "正在从公开接口获取应用信息...");
    try {
      const meta = await fetchAppMetadata(Number(gameForm.appid));
      setGameForm((current) => ({ ...current, name: meta.name }));
      pushConsole("success", `已从 ${meta.source} 获取 ${meta.name}`);
    } catch (err) {
      pushConsole("error", `自动获取失败，可手动补齐：${String(err)}`);
    } finally {
      setBusy("idle");
    }
  }

  function handleHeaderMouseDown(event: MouseEvent<HTMLElement>) {
    if (event.button !== 0 || !hasTauriRuntime()) return;
    if ((event.target as HTMLElement).closest(".window-actions")) return;

    if (event.detail === 2) {
      void toggleMaximizeWindow();
      return;
    }

    void getCurrentWindow().startDragging();
  }

  function stopWindowDrag(event: MouseEvent<HTMLElement>) {
    event.stopPropagation();
  }

  return (
    <div className="app-viewport">
      <main className="app-shell" style={{ transform: `scale(${canvasScale})` }}>
        <header className="app-header" data-tauri-drag-region onMouseDown={handleHeaderMouseDown}>
          <div data-tauri-drag-region>
            <h1>G-OpenSteamTool 管理器</h1>
            <div className="status-line" data-tauri-drag-region>
              <StatusDot ok={Boolean(state?.steam_running)} />
              <span>{state?.steam_running ? "Steam 运行中" : "Steam 未运行"}</span>
              <span>版本 {state?.steam_version ?? "未检测"}</span>
              <span>DLL {dllSummary}</span>
              <span>Lua {state?.lua_count ?? games.length} 个</span>
              <span>TOML {state?.config_exists ? "存在" : "未创建"}</span>
            </div>
          </div>
          <div className="window-actions" onMouseDown={stopWindowDrag} onDoubleClick={stopWindowDrag}>
            <button onClick={() => void minimizeWindow()} title="最小化" aria-label="最小化">
              <span />
            </button>
            <button onClick={() => void toggleMaximizeWindow()} title="最大化" aria-label="最大化">
              <Square size={15} />
            </button>
            <button onClick={() => void closeWindow()} title="关闭" aria-label="关闭">
              <X size={18} />
            </button>
          </div>
        </header>

        <nav className="tabbar" aria-label="主导航">
          {tabs.map((item) => {
            const Icon = item.icon;
            return (
              <button key={item.id} className={tab === item.id ? "active" : ""} onClick={() => setTab(item.id)}>
                <Icon size={22} />
                {item.label}
              </button>
            );
          })}
        </nav>

        <StatusConsole state={consoleState} onClear={clearConsole} />

        <section className="content">
          {tab === "overview" && (
            <Overview
              busy={busy}
              steamDir={steamDir}
              state={state}
              settings={settings}
              games={games}
              onChoose={chooseSteamDir}
              onRefresh={() => refreshAll()}
              onInitialize={initializeApp}
              onCloseSteam={() => runAction(closeSteam, "Steam 已关闭", "正在关闭 Steam...")}
              onRestartSteam={() => runAction(() => restartSteam(steamDir), "Steam 已快速重启", "正在关闭并重启 Steam...")}
            />
          )}
          {tab === "lua" && (
            <LuaPanel
              busy={busy}
              steamDir={steamDir}
              games={games}
              form={gameForm}
              onForm={setGameForm}
              onSave={saveGame}
              onEdit={editGame}
              onImport={importLua}
              onOpenDir={() => runAction(() => openLuaDir(steamDir), "Lua 目录已打开")}
              onToggleEnabled={(game, enabled) =>
                runAction(
                  async () => {
                    await setGameEnabled(steamDir, game.appid, enabled);
                    setGameForm((current) => (current.appid === game.appid ? { ...current, enabled } : current));
                  },
                  enabled ? `已启用 G-${game.appid}.lua` : `已禁用 G-${game.appid}.lua`
                )
              }
              onDelete={(appid) => runAction(() => deleteGame(steamDir, appid), `已删除 G-${appid}.lua`)}
            />
          )}
          {tab === "dll" && (
            <div className="dll-page">
              <div className="actions-row dll-page-actions">
                <button className="primary" onClick={() => runAction(() => installDlls(steamDir), "DLL 已安装", "正在安装 DLL...")} disabled={!steamDir || !state?.dll_resources_ready}>
                  <Download size={18} />
                  安装 DLL
                </button>
                <button onClick={() => runAction(() => removeDlls(steamDir), "DLL 已移除", "正在移除 DLL...")} disabled={!steamDir}>
                  <Trash2 size={18} />
                  移除 DLL
                </button>
                <button onClick={() => refreshAll()} disabled={!steamDir}>
                  <RefreshCcw size={18} />
                  刷新
                </button>
              </div>
              <DllPanel state={state} />
            </div>
          )}
          {tab === "settings" && (
            <SettingsPanel
              settings={settings}
              luaPathsText={luaPathsText}
              githubDnsOptimization={githubDnsOptimization}
              onSettings={setSettings}
              onLuaPathsText={setLuaPathsText}
              onGithubDnsOptimization={setGithubDnsOptimization}
              onSave={saveCurrentSettings}
            />
          )}
          {tab === "logs" && (
            <BetterLogsPanel
              logs={logs}
              active={activeLog}
              selected={selectedLog}
              onSelect={setSelectedLog}
              onRefresh={() => refreshAll()}
            />
          )}
          {tab === "about" && <BetterAboutPanel githubDnsOptimization={githubDnsOptimization} />}
        </section>

        <footer className="footer">
          <button onClick={() => setTab("about")}>
            <BookOpen size={18} />
            帮助文档
          </button>
          <button onClick={() => setTab("logs")}>
            <Activity size={18} />
            查看日志
          </button>
        </footer>
      </main>
    </div>
  );
}

function useCanvasScale() {
  const readScale = () => {
    if (typeof window === "undefined") return 1;
    return Math.min(window.innerWidth / CANVAS_WIDTH, window.innerHeight / CANVAS_HEIGHT, 1);
  };

  const [scale, setScale] = useState(readScale);

  useEffect(() => {
    const updateScale = () => setScale(readScale());
    updateScale();
    window.addEventListener("resize", updateScale);
    return () => window.removeEventListener("resize", updateScale);
  }, []);

  return scale;
}

function currentTime() {
  return new Date().toLocaleTimeString("zh-CN", { hour12: false });
}

function normalizeGameForm(game: GameConfig): GameConfig {
  const appid = Number(game.appid) || 0;
  const entries = (game.appid_entries ?? [])
    .map((entry) => ({
      appid: Number(entry.appid) || 0,
      unlock_flag: entry.unlock_flag == null ? null : Number(entry.unlock_flag) || 0,
      depot_key: entry.depot_key ?? "",
    }))
    .filter((entry) => entry.appid > 0);

  const fallbackEntries: AppIdEntry[] =
    entries.length > 0
      ? entries
      : appid
        ? [{ appid, unlock_flag: null, depot_key: game.depot_key ?? "" }]
        : [];

  return {
    appid,
    name: game.name ?? "",
    enabled: game.enabled ?? true,
    depot_key: game.depot_key ?? "",
    access_token: game.access_token ?? "",
    manifest_gid: game.manifest_gid ?? "",
    app_ticket_hex: game.app_ticket_hex ?? "",
    e_ticket_hex: game.e_ticket_hex ?? "",
    stat_steam_id: game.stat_steam_id ?? "",
    appid_entries: fallbackEntries,
  };
}

function StatusConsole({ state, onClear }: { state: ConsoleState; onClear: () => void }) {
  const Icon = state.kind === "success" ? CheckCircle2 : state.kind === "error" ? XCircle : state.kind === "working" ? RefreshCcw : Activity;
  const label = state.kind === "success" ? "成功" : state.kind === "error" ? "失败" : state.kind === "working" ? "处理中" : "空闲";

  return (
    <div className={`status-console ${state.kind}`}>
      <div>
        <Icon size={20} />
        <strong>状态控制台</strong>
        <span>{label}</span>
        <p>{state.text}</p>
      </div>
      <div>
        <time>{state.time}</time>
        <button onClick={onClear}>清除</button>
      </div>
    </div>
  );
}

function Overview({
  busy,
  steamDir,
  state,
  settings,
  games,
  onChoose,
  onRefresh,
  onInitialize,
  onCloseSteam,
  onRestartSteam,
}: {
  busy: Busy;
  steamDir: string;
  state: ScanState | null;
  settings: ManagerSettings;
  games: GameConfig[];
  onChoose: () => void;
  onRefresh: () => void;
  onInitialize: () => void;
  onCloseSteam: () => void;
  onRestartSteam: () => void;
}) {
  return (
    <div className="overview-grid">
      <section className="panel init-panel">
        <PanelTitle icon={PlayCircle} title="应用初始化" extra={`初始化状态：${isInitializedReady(state) ? "已就绪" : "未初始化"}`} />
        <div className="path-row">
          <Folder size={24} />
          <input value={steamDir || "请选择 Steam 根目录"} readOnly />
          <button className="icon-button" onClick={onChoose} title="选择 Steam 目录">
            <Folder size={22} />
          </button>
        </div>
        <div className="actions-row">
          <button className="primary" onClick={onRefresh} disabled={!steamDir || busy !== "idle"}>
            <RefreshCcw size={19} />
            刷新状态
          </button>
          <button onClick={onInitialize} disabled={!steamDir || busy !== "idle" || state?.dll_resources_ready === false}>
            <Download size={18} />
            初始化
          </button>
          <button onClick={onCloseSteam} disabled={!steamDir}>
            <Square size={18} />
            关闭 Steam
          </button>
          <button onClick={onRestartSteam} disabled={!steamDir}>
            <RotateCw size={18} />
            快速重启 Steam
          </button>
        </div>
      </section>

      <div className="metric-grid overview-metrics">
        <Metric icon={Gauge} label="Steam 版本" value={state?.steam_version ?? "未检测"} />
        <Metric icon={Code2} label="Lua 配置" value={`${games.length} 个`} />
        <Metric icon={Box} label="DLL 状态" value={summarizeDllOverview(state?.dlls ?? [])} tone="violet" />
      </div>

      <section className="panel dll-list-panel">
        <PanelTitle icon={Box} title="DLL 管理" extra={`${state?.dlls.length ?? 0} 个 DLL`} />
        <div className="table">
          {(state?.dlls ?? []).map((dll) => (
            <div className="table-row" key={dll.name}>
              <span>{stateIcon(dll.state)}</span>
              <strong>{dll.name}</strong>
              <span className={overviewDllClass(dll)}>{overviewDllLabel(dll)}</span>
              <ChevronRight size={18} />
            </div>
          ))}
        </div>
      </section>

      <section className="panel fetch-panel">
        <PanelTitle icon={Download} title="自动获取 / Auto-Fetcher" extra="公开接口，无需 Key" />
        <p className="soft-text">在 Lua 页面输入应用ID AppId 可获取基础应用名；DLC、Depot、Manifest 仍可手动补齐。</p>
      </section>

      <section className="panel settings-summary">
        <PanelTitle icon={Settings} title="设置概览" />
        <SummaryLine label="清单来源" value={settings.manifest_url} />
        <SummaryLine label="日志等级" value={settings.log_level} />
        <SummaryLine label="额外 Lua 路径" value={`${settings.lua_paths.length} 条`} />
        <SummaryLine label="签名镜像" value={settings.pattern_mirror || "默认"} />
      </section>

      <section className="panel about-summary">
        <MiniAboutCard />
      </section>
    </div>
  );
}

function LuaPanel({
  busy,
  steamDir,
  games,
  form,
  onForm,
  onSave,
  onEdit,
  onImport,
  onOpenDir,
  onToggleEnabled,
  onDelete,
}: {
  busy: Busy;
  steamDir: string;
  games: GameConfig[];
  form: GameConfig;
  onForm: (form: GameConfig) => void;
  onSave: () => void;
  onEdit: (form: GameConfig) => void;
  onImport: () => void;
  onOpenDir: () => void;
  onToggleEnabled: (game: GameConfig, enabled: boolean) => void;
  onDelete: (appid: number) => void;
}) {
  return (
    <div className="two-column lua-layout">
      <section className="panel">
        <PanelTitle icon={ListChecks} title="Lua 文件" extra={`${games.length} 个`} />
        <div className="actions-row lua-file-actions">
          <button onClick={onImport} disabled={!steamDir || busy !== "idle"}>
            <Download size={18} />
            导入 Lua
          </button>
          <button onClick={onOpenDir} disabled={!steamDir || busy !== "idle"}>
            <Folder size={18} />
            打开目录
          </button>
        </div>
        <div className="game-list">
          {games.map((game) => (
            <div
              className={`game-row ${form.appid === game.appid ? "active" : ""} ${game.enabled ? "" : "disabled"}`}
              key={game.appid}
              role="button"
              tabIndex={0}
              onClick={() => onEdit(game)}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  onEdit(game);
                }
              }}
            >
              <button
                className={`lua-switch ${game.enabled ? "on" : ""}`}
                aria-pressed={game.enabled}
                title={game.enabled ? "禁用 Lua" : "启用 Lua"}
                disabled={!steamDir || busy !== "idle"}
                onClick={(event) => {
                  event.stopPropagation();
                  onToggleEnabled(game, !game.enabled);
                }}
              >
                <span />
              </button>
              <div>
                <strong>{game.name || `App ${game.appid}`}</strong>
                <span>G-{game.appid}.lua{game.enabled ? "" : ".disabled"}</span>
              </div>
              <span>{game.enabled ? "启用" : "已禁用"}</span>
              <span>{game.appid_entries?.length || 1} Depot</span>
              <button
                className="danger"
                onClick={(event) => {
                  event.stopPropagation();
                  onDelete(game.appid);
                }}
              >
                <Trash2 size={17} />
              </button>
            </div>
          ))}
          {games.length === 0 && <p className="empty">还没有 G-*.lua 配置。</p>}
        </div>
      </section>

      <section className="panel lua-editor-panel">
        <PanelTitle icon={Code2} title="Lua 配置编辑" extra="生成 G-<appid>.lua" />
        <div className="lua-editor-scroll">
          <div className="form-grid">
            <Field label="应用ID AppId" value={String(form.appid || "")} onChange={(value) => onForm({ ...form, appid: Number(value) || 0 })} />
            <Field label="游戏名称 Name" value={form.name} onChange={(value) => onForm({ ...form, name: value })} />
            <Field label="访问令牌 Access Token" value={form.access_token ?? ""} onChange={(value) => onForm({ ...form, access_token: value })} />
            <Field label="清单ID Manifest GID" value={form.manifest_gid ?? ""} onChange={(value) => onForm({ ...form, manifest_gid: value })} />
          </div>
          <DepotKeysEditor form={form} onForm={onForm} />
          <div className="form-grid">
            <Field label="应用票据 AppTicket Hex" value={form.app_ticket_hex ?? ""} onChange={(value) => onForm({ ...form, app_ticket_hex: value })} wide />
            <Field label="加密票据 ETicket Hex" value={form.e_ticket_hex ?? ""} onChange={(value) => onForm({ ...form, e_ticket_hex: value })} wide />
            <Field label="成就统计 SteamID Stat SteamID" value={form.stat_steam_id ?? ""} onChange={(value) => onForm({ ...form, stat_steam_id: value })} />
          </div>
        </div>
        <div className="actions-row">
          <button className="primary" onClick={onSave} disabled={!steamDir || !form.appid || busy !== "idle"}>
            <Save size={18} />
            保存 Lua
          </button>
        </div>
      </section>
    </div>
  );
}

function DllPanel({ state }: { state: ScanState | null }) {
  return (
    <section className="panel">
      <PanelTitle icon={Box} title="DLL 文件状态" extra={state?.dll_resources_ready ? "资源就绪" : "资源缺失"} />
      <div className="dll-status-list">
        {(state?.dlls ?? []).map((dll) => (
          <div className="dll-status-card" key={dll.name}>
            <div className="dll-status-head">
              <div>
                {stateIcon(dll.state)}
                <strong>{dll.name}</strong>
              </div>
              <span className={`dll-status-badge ${dllClass(dll)}`}>{dllLabel(dll)}</span>
            </div>
            <div className="hash-row">
              <span>资源哈希</span>
              <code>{formatHash(dll.resource_hash)}</code>
            </div>
            <div className="hash-row">
              <span>目标哈希</span>
              <code>{formatHash(dll.target_hash)}</code>
            </div>
          </div>
        ))}
      </div>
      {!state?.dll_resources_ready && (
        <div className="warning">
          <ShieldAlert size={20} />
          缺少资源：{state?.missing_dll_resources.join(", ") || "未扫描"}
        </div>
      )}
    </section>
  );
}

function SettingsPanel({
  settings,
  luaPathsText,
  githubDnsOptimization,
  onSettings,
  onLuaPathsText,
  onGithubDnsOptimization,
  onSave,
}: {
  settings: ManagerSettings;
  luaPathsText: string;
  githubDnsOptimization: boolean;
  onSettings: (settings: ManagerSettings) => void;
  onLuaPathsText: (text: string) => void;
  onGithubDnsOptimization: (enabled: boolean) => void;
  onSave: () => void;
}) {
  return (
    <section className="panel">
      <PanelTitle icon={Settings} title="opensteamtool.toml" extra="保存后重启 Steam 生效" />
      <div className="form-grid settings-grid">
        <label>
          日志等级
          <select value={settings.log_level} onChange={(event) => onSettings({ ...settings, log_level: event.target.value })}>
            {["trace", "debug", "info", "warn", "error"].map((level) => (
              <option key={level}>{level}</option>
            ))}
          </select>
        </label>
        <label>
          Manifest 来源
          <select value={settings.manifest_url} onChange={(event) => onSettings({ ...settings, manifest_url: event.target.value })}>
            <option value="wudrm">wudrm</option>
            <option value="steamrun">steamrun</option>
          </select>
        </label>
        <NumberField label="解析超时 Resolve Timeout" value={settings.timeout_resolve_ms} onChange={(value) => onSettings({ ...settings, timeout_resolve_ms: value })} />
        <NumberField label="连接超时 Connect Timeout" value={settings.timeout_connect_ms} onChange={(value) => onSettings({ ...settings, timeout_connect_ms: value })} />
        <NumberField label="发送超时 Send Timeout" value={settings.timeout_send_ms} onChange={(value) => onSettings({ ...settings, timeout_send_ms: value })} />
        <NumberField label="接收超时 Recv Timeout" value={settings.timeout_recv_ms} onChange={(value) => onSettings({ ...settings, timeout_recv_ms: value })} />
        <label className="wide">
          签名镜像 Pattern Mirror
          <input value={settings.pattern_mirror} onChange={(event) => onSettings({ ...settings, pattern_mirror: event.target.value })} placeholder="默认留空" />
        </label>
        <label className="wide">
          额外 Lua 路径 Lua Paths
          <textarea value={luaPathsText} onChange={(event) => onLuaPathsText(event.target.value)} placeholder="每行一个路径" />
        </label>
      </div>
      <div className="settings-option">
        <div>
          <strong>GitHub DNS 优化</strong>
          <span>仅应用内 GitHub Release 检查使用 DoT；不修改系统 DNS、hosts 或代理。</span>
        </div>
        <button
          className={`toggle-button ${githubDnsOptimization ? "on" : ""}`}
          aria-pressed={githubDnsOptimization}
          onClick={() => onGithubDnsOptimization(!githubDnsOptimization)}
        >
          <span />
          {githubDnsOptimization ? "已开启" : "已关闭"}
        </button>
      </div>
      <div className="actions-row">
        <button className="primary" onClick={onSave}>
          <Save size={18} />
          保存 TOML
        </button>
      </div>
    </section>
  );
}

function LogsPanel({
  logs,
  active,
  selected,
  onSelect,
  onRefresh,
}: {
  logs: LogFile[];
  active?: LogFile;
  selected: string;
  onSelect: (name: string) => void;
  onRefresh: () => void;
}) {
  return (
    <div className="two-column logs-layout">
      <section className="panel">
        <PanelTitle icon={ListChecks} title="日志文件" extra={`${logs.length} 个`} />
        <div className="log-list">
          {logs.map((log) => (
            <button key={log.name} className={selected === log.name ? "active" : ""} onClick={() => onSelect(log.name)}>
              {log.name}
            </button>
          ))}
          {logs.length === 0 && <p className="empty">暂无日志。</p>}
        </div>
        <button onClick={onRefresh}>
          <RefreshCcw size={18} />
          刷新日志
        </button>
      </section>
      <section className="panel log-viewer">
        <PanelTitle icon={Activity} title={active?.name ?? "日志内容"} />
        <pre>{active?.content ?? "选择 Steam 目录后可读取 <Steam>\\opensteamtool\\*.log"}</pre>
      </section>
    </div>
  );
}

function AboutPanel() {
  return (
    <section className="about-card">
      <div className="about-info">
        <PanelTitle icon={Info} title="关于 G-OpenSteamTool" />
        <div className="about-detail-box">
          <AboutLine icon={Gauge} label="版本" value={APP_VERSION} />
          <AboutLine icon={Activity} label="作者" value="G-Yoka" />
          <AboutLine icon={Info} label="项目地址" value={PROJECT_URL} link />
        </div>
      </div>
      <div className="about-art">
        <img src={yokaStarMoonFlower} alt="Yoka 星月花" />
      </div>
    </section>
  );
}

function MiniAboutCard() {
  return (
    <div className="mini-about-card">
      <div className="mini-about-info">
        <PanelTitle icon={Info} title="关于 G-OpenSteamTool" />
        <div className="mini-about-detail">
          <AboutLine icon={Gauge} label="版本" value={APP_VERSION} />
          <AboutLine icon={Activity} label="作者" value="G-Yoka" />
          <AboutLine icon={Info} label="项目地址" value="G-Yoka/G-OpenSteamTool" />
        </div>
      </div>
      <div className="mini-about-art">
        <img src={yokaStarMoonFlower} alt="Yoka 星月花" />
      </div>
    </div>
  );
}

function AboutLine({
  icon: Icon,
  label,
  value,
  link,
}: {
  icon: typeof Gauge;
  label: string;
  value: string;
  link?: boolean;
}) {
  return (
    <div className="about-line">
      <Icon size={20} />
      <span>{label}</span>
      {link ? (
        <a href={value} target="_blank" rel="noreferrer">
          {value}
        </a>
      ) : (
        <strong>{value}</strong>
      )}
    </div>
  );
}

function Metric({ icon: Icon, label, value, tone }: { icon: typeof Gauge; label: string; value: string; tone?: "violet" }) {
  return (
    <section className={`metric ${tone ?? ""}`}>
      <Icon size={58} />
      <span>{label}</span>
      <strong>{value}</strong>
    </section>
  );
}

function PanelTitle({ icon: Icon, title, extra }: { icon: typeof Gauge; title: string; extra?: string }) {
  return (
    <div className="panel-title">
      <div>
        <Icon size={25} />
        <h2>{title}</h2>
      </div>
      {extra && <span>{extra}</span>}
    </div>
  );
}

function Field({ label, value, onChange, wide }: { label: string; value: string; onChange: (value: string) => void; wide?: boolean }) {
  return (
    <label className={wide ? "wide" : ""}>
      {label}
      <input value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function DepotKeysEditor({ form, onForm }: { form: GameConfig; onForm: (form: GameConfig) => void }) {
  const entries = form.appid_entries ?? [];
  const [selectedRows, setSelectedRows] = useState<number[]>([]);

  const updateEntry = (index: number, patch: Partial<AppIdEntry>) => {
    onForm({
      ...form,
      appid_entries: entries.map((entry, entryIndex) => (entryIndex === index ? { ...entry, ...patch } : entry)),
    });
  };

  const toggleRow = (index: number) => {
    setSelectedRows((current) => (current.includes(index) ? current.filter((item) => item !== index) : [...current, index]));
  };

  const addDepot = () => {
    onForm({
      ...form,
      appid_entries: [...entries, { appid: 0, unlock_flag: 0, depot_key: "" }],
    });
  };

  const deleteSelected = () => {
    onForm({
      ...form,
      appid_entries: entries.filter((_, index) => !selectedRows.includes(index)),
    });
    setSelectedRows([]);
  };

  return (
    <fieldset className="depot-editor">
      <legend>Depot 与解密密钥 Depots / Decryption Keys</legend>
      <div className="depot-toolbar">
        <button type="button" onClick={addDepot}>
          添加 Depot
        </button>
        <button type="button" onClick={deleteSelected} disabled={selectedRows.length === 0}>
          删除选中
        </button>
      </div>
      <div className="depot-table">
        <div className="depot-table-head">
          <span />
          <span>DepotId</span>
          <span>解密密钥</span>
        </div>
        {entries.map((entry, index) => (
          <div className="depot-table-row" key={`${entry.appid}-${index}`}>
            <input
              aria-label={`选择 Depot ${index + 1}`}
              type="checkbox"
              checked={selectedRows.includes(index)}
              onChange={() => toggleRow(index)}
            />
            <input
              aria-label="DepotId"
              type="number"
              min={0}
              value={entry.appid || ""}
              onChange={(event) => updateEntry(index, { appid: Number(event.target.value) || 0 })}
            />
            <input
              aria-label="解密密钥"
              value={entry.depot_key ?? ""}
              onChange={(event) => updateEntry(index, { depot_key: event.target.value, unlock_flag: entry.unlock_flag ?? 0 })}
            />
          </div>
        ))}
        {entries.length === 0 && <div className="depot-empty">暂无 Depot，保存时会按应用ID生成基础 addappid。</div>}
      </div>
    </fieldset>
  );
}

function NumberField({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return (
    <label>
      {label}
      <input type="number" min={0} value={value} onChange={(event) => onChange(Number(event.target.value) || 0)} />
    </label>
  );
}

function SummaryLine({ label, value }: { label: string; value: string }) {
  return (
    <div className="summary-line">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function StatusDot({ ok }: { ok: boolean }) {
  return <span className={`status-dot ${ok ? "ok" : ""}`} />;
}

function isInitializedReady(state: ScanState | null) {
  return Boolean(
    state?.steam_valid &&
      state.config_exists &&
      state.dlls.length === 3 &&
      state.dlls.every((dll) => dll.state === "Managed")
  );
}

function summarizeDllOverview(dlls: DllStatus[]) {
  if (dlls.length === 0) return "未扫描";
  if (dlls.every((dll) => dll.load_state === "SteamNotRunning")) return "Steam未运行";
  if (dlls.some((dll) => dll.load_state === "VerifyFailed")) return "无法验证";
  if (dlls.some((dll) => dll.loaded_by_steam)) return "已加载";
  return "未加载";
}

function summarizeDllLoads(dlls: DllStatus[]) {
  if (dlls.length === 0) return "0/3 未扫描";
  if (dlls.every((dll) => dll.load_state === "SteamNotRunning")) return "Steam 未运行";
  if (dlls.some((dll) => dll.load_state === "VerifyFailed")) return "无法验证";
  const loaded = dlls.filter((dll) => dll.loaded_by_steam).length;
  return `${loaded}/${dlls.length} 已旁路加载`;
}

function dllLoadLabel(dll: DllStatus) {
  if (dll.load_state === "Loaded") return "已旁路加载";
  if (dll.load_state === "NotLoaded") return "未加载";
  if (dll.load_state === "VerifyFailed") return "无法验证";
  return "Steam 未运行";
}

function dllLoadClass(dll: DllStatus) {
  if (dll.load_state === "Loaded") return "ok-text";
  if (dll.load_state === "VerifyFailed") return "warn-text";
  return "muted-text";
}

function summarizeDlls(dlls: DllStatus[]) {
  if (dlls.length === 0) return "0/3 未扫描";
  const managed = dlls.filter((dll) => dll.state === "Managed").length;
  return `${managed}/${dlls.length} 已匹配`;
}

function dllLabel(dll: DllStatus) {
  if (!dll.resource_hash) return "资源缺失";
  if (!dll.target_hash) return "未安装";
  return dll.hash_matched ? "一致" : "不一致";
}

function dllClass(dll: DllStatus) {
  if (dll.hash_matched) return "ok-text";
  if (!dll.resource_hash || !dll.target_hash) return "muted-text";
  return "warn-text";
}

function overviewDllLabel(dll: DllStatus) {
  if (dll.state === "Managed") return "已安装";
  if (dll.state === "Foreign") return "不一致";
  return "未安装";
}

function overviewDllClass(dll: DllStatus) {
  return dll.state === "Managed" ? "ok-text" : dll.state === "Foreign" ? "warn-text" : "muted-text";
}

function formatHash(hash?: string | null) {
  return hash || "--";
}

function stateIcon(state: string) {
  if (state === "Managed") return <CheckCircle2 className="ok-icon" size={20} />;
  if (state === "Foreign") return <ShieldAlert className="warn-icon" size={20} />;
  return <XCircle className="muted-icon" size={20} />;
}

type ParsedLogRow = {
  raw: string;
  time: string;
  level: string | null;
  message: string;
};

function BetterLogsPanel({
  logs,
  active,
  selected,
  onSelect,
  onRefresh,
}: {
  logs: LogFile[];
  active?: LogFile;
  selected: string;
  onSelect: (name: string) => void;
  onRefresh: () => void;
}) {
  const [level, setLevel] = useState("all");
  const [query, setQuery] = useState("");
  const [wrap, setWrap] = useState(true);
  const [autoScroll, setAutoScroll] = useState(true);
  const viewerRef = useRef<HTMLDivElement | null>(null);
  const rows = useMemo(() => parseLogRows(active?.content ?? ""), [active]);
  const filteredRows = useMemo(
    () =>
      rows.filter((row) => {
        const levelMatched = level === "all" || row.level === level;
        const queryMatched = !query.trim() || row.raw.toLowerCase().includes(query.trim().toLowerCase());
        return levelMatched && queryMatched;
      }),
    [rows, level, query]
  );

  useEffect(() => {
    if (autoScroll && viewerRef.current) {
      viewerRef.current.scrollTop = viewerRef.current.scrollHeight;
    }
  }, [active, filteredRows.length, autoScroll]);

  return (
    <div className="two-column logs-layout">
      <section className="panel log-files-panel">
        <PanelTitle icon={ListChecks} title="日志文件" extra={`${logs.length} 个`} />
        <div className="log-list">
          {logs.map((log) => (
            <button key={log.name} className={selected === log.name ? "active" : ""} onClick={() => onSelect(log.name)}>
              <strong>{log.name}</strong>
              <span>{log.line_count} 行 · {formatBytes(log.size_bytes)}</span>
              <small>{formatLogTime(log.modified_time)}</small>
            </button>
          ))}
          {logs.length === 0 && <p className="empty">暂无日志。</p>}
        </div>
        <button onClick={onRefresh}>
          <RefreshCcw size={18} />
          刷新日志
        </button>
      </section>
      <section className="panel log-viewer">
        <PanelTitle icon={Activity} title={active?.name ?? "日志内容"} extra={active ? `${filteredRows.length}/${rows.length} 行` : undefined} />
        <div className="log-toolbar">
          <select value={level} onChange={(event) => setLevel(event.target.value)}>
            {["all", "trace", "debug", "info", "warn", "error"].map((item) => (
              <option key={item} value={item}>
                {item === "all" ? "全部" : item}
              </option>
            ))}
          </select>
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索日志内容" />
          <label>
            <input type="checkbox" checked={wrap} onChange={(event) => setWrap(event.target.checked)} />
            换行
          </label>
          <label>
            <input type="checkbox" checked={autoScroll} onChange={(event) => setAutoScroll(event.target.checked)} />
            滚底
          </label>
        </div>
        <div className={`log-lines ${wrap ? "wrap" : ""}`} ref={viewerRef}>
          {active ? (
            filteredRows.map((row, index) => (
              <div className={`log-line ${row.level ?? "plain"}`} key={`${index}-${row.raw}`}>
                <time>{row.time || "--"}</time>
                <span>{row.level || "log"}</span>
                <p>{row.message}</p>
              </div>
            ))
          ) : (
            <p className="empty">选择 Steam 目录后可读取 &lt;Steam&gt;\\opensteamtool\\*.log</p>
          )}
        </div>
      </section>
    </div>
  );
}

function parseLogRows(content: string): ParsedLogRow[] {
  return content
    .split(/\r?\n/)
    .filter((line) => line.length > 0)
    .map((line) => {
      const match = line.match(/^(?<time>\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?|\d{2}:\d{2}:\d{2}(?:\.\d+)?)?\s*(?:\[?(?<level>trace|debug|info|warn|warning|error|fatal)\]?)?\s*(?<message>.*)$/i);
      const parsedLevel = match?.groups?.level?.toLowerCase() ?? null;
      return {
        raw: line,
        time: match?.groups?.time ?? "",
        level: parsedLevel === "warning" || parsedLevel === "fatal" ? (parsedLevel === "fatal" ? "error" : "warn") : parsedLevel,
        message: match?.groups?.message?.trim() || line,
      };
    });
}

function formatBytes(bytes?: number) {
  if (!bytes) return "0 B";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function formatLogTime(value?: number | null) {
  if (!value) return "未知时间";
  return new Date(value * 1000).toLocaleString("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function BetterAboutPanel({ githubDnsOptimization }: { githubDnsOptimization: boolean }) {
  const [checking, setChecking] = useState(false);
  const [installing, setInstalling] = useState(false);
  const [updateMessage, setUpdateMessage] = useState("使用 GitHub Releases 检查正式版本。");
  const [updateInfo, setUpdateInfo] = useState<{ version: string; date?: string | null; body?: string; available: boolean } | null>(null);
  const [releaseInfo, setReleaseInfo] = useState<GitHubReleaseInfo | null>(null);
  const updateRef = useRef<Update | null>(null);

  async function handleCheckUpdate() {
    setChecking(true);
    setUpdateMessage("正在检查 GitHub Releases...");
    setUpdateInfo(null);
    setReleaseInfo(null);
    updateRef.current = null;
    try {
      const update = hasTauriRuntime() ? await check() : null;
      if (update?.available) {
        updateRef.current = update;
        setUpdateInfo({ version: update.version, date: update.date, body: update.body, available: true });
        setUpdateMessage(`发现新版本 ${update.version}`);
      } else {
        setUpdateInfo({ version: APP_VERSION, available: false });
        setUpdateMessage("当前已是最新版本。");
      }
    } catch (err) {
      setUpdateMessage(`正式更新检查失败，正在使用 GitHub API 诊断：${String(err)}`);
    }

    try {
      const release = await checkGithubRelease(githubDnsOptimization);
      setReleaseInfo(release);
      if (!updateRef.current && compareVersions(release.version, APP_VERSION) > 0) {
        setUpdateInfo({ version: release.version, date: release.published_at, body: release.body, available: true });
        setUpdateMessage(`GitHub Releases 有新版本 ${release.version}；可在 Release 页面手动下载。`);
      }
    } catch (err) {
      setUpdateMessage((current) => `${current}\nGitHub API 诊断失败：${String(err)}`);
    } finally {
      setChecking(false);
    }
  }

  async function handleInstallUpdate() {
    if (!updateRef.current) {
      setUpdateMessage("当前没有可直接安装的 updater 包；请确认 Release 已上传 latest.json 与签名安装包。");
      return;
    }
    setInstalling(true);
    setUpdateMessage("正在下载并安装更新...");
    try {
      await updateRef.current.downloadAndInstall();
      setUpdateMessage("更新已安装，点击重启应用完成切换。");
    } catch (err) {
      setUpdateMessage(`更新安装失败：${String(err)}`);
    } finally {
      setInstalling(false);
    }
  }

  return (
    <section className="about-page">
      <div className="about-card">
        <div className="about-info">
          <PanelTitle icon={Info} title="关于 G-OpenSteamTool" />
          <div className="about-detail-box">
            <AboutLine icon={Gauge} label="版本" value={APP_VERSION} />
            <AboutLine icon={Activity} label="作者" value="G-Yoka" />
            <AboutLine icon={Info} label="项目地址" value={PROJECT_URL} link />
          </div>
        </div>
        <div className="about-art">
          <img src={yokaStarMoonFlower} alt="Yoka 星月花" />
        </div>
      </div>
      <section className="panel update-panel">
        <PanelTitle icon={Download} title="在线更新" extra={githubDnsOptimization ? "GitHub DoT 已开启" : "GitHub DoT 未开启"} />
        <div className="update-grid">
          <SummaryLine label="当前版本" value={APP_VERSION} />
          <SummaryLine label="最新版本" value={updateInfo?.version ?? releaseInfo?.version ?? "未检查"} />
          <SummaryLine label="发布时间" value={updateInfo?.date ?? releaseInfo?.published_at ?? "未知"} />
          <SummaryLine label="Release 资源" value={releaseInfo ? `${releaseInfo.assets.length} 个` : "未读取"} />
        </div>
        <p className="update-message">{updateMessage}</p>
        {(updateInfo?.body || releaseInfo?.body) && <pre className="release-notes">{trimReleaseNotes(updateInfo?.body || releaseInfo?.body || "")}</pre>}
        {releaseInfo?.resolved_hosts?.length ? (
          <div className="resolved-hosts">
            {releaseInfo.resolved_hosts.map((host) => (
              <span key={host.host}>{host.host}: {host.addresses.slice(0, 2).join(", ")}</span>
            ))}
          </div>
        ) : null}
        <div className="actions-row">
          <button className="primary" onClick={handleCheckUpdate} disabled={checking || installing}>
            <RefreshCcw size={18} />
            检查更新
          </button>
          <button onClick={handleInstallUpdate} disabled={!updateRef.current || checking || installing}>
            <Download size={18} />
            下载并安装
          </button>
          <button onClick={() => void relaunch()} disabled={!hasTauriRuntime()}>
            <RotateCw size={18} />
            重启应用
          </button>
        </div>
      </section>
    </section>
  );
}

function trimReleaseNotes(value: string) {
  const lines = value.trim().split(/\r?\n/).slice(0, 8);
  return lines.join("\n");
}

function compareVersions(a: string, b: string) {
  const left = a.replace(/^v/i, "").split(/[.-]/).map((part) => Number(part) || 0);
  const right = b.replace(/^v/i, "").split(/[.-]/).map((part) => Number(part) || 0);
  const max = Math.max(left.length, right.length);
  for (let index = 0; index < max; index += 1) {
    if ((left[index] ?? 0) > (right[index] ?? 0)) return 1;
    if ((left[index] ?? 0) < (right[index] ?? 0)) return -1;
  }
  return 0;
}

export default App;
