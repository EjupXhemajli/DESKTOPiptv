/**
 * Backend-Bridge.
 *
 * In der Tauri-Shell laufen alle Aufrufe über `invoke` gegen das
 * Rust-Backend. Läuft die App im reinen Browser (Vite-Dev ohne Tauri,
 * UI-Entwicklung), springt ein In-Memory-Mock ein, damit die Oberfläche
 * ohne native Schicht entwickelt und geprüft werden kann.
 * Der Mock persistiert nichts und ist klar als solcher gekennzeichnet.
 */
import type {
  Channel, ChannelGroup, Diagnostics, ImportReport, Provider, ProviderKind,
} from "./types";

export const isTauri = "__TAURI_INTERNALS__" in window;

type Listener = (p: unknown) => void;

interface Backend {
  listProviders(): Promise<Provider[]>;
  addProvider(input: { name: string; kind: ProviderKind; source: string; username?: string; password?: string }): Promise<number>;
  deleteProvider(id: number): Promise<void>;
  importM3uFromUrl(providerId: number, url: string): Promise<ImportReport>;
  importM3uFromFile(providerId: number, path: string): Promise<ImportReport>;
  listGroups(providerId: number): Promise<ChannelGroup[]>;
  listChannels(providerId: number, groupId: number | null, limit: number, offset: number): Promise<Channel[]>;
  searchChannels(query: string): Promise<Channel[]>;
  getSetting(key: string): Promise<string | null>;
  setSetting(key: string, value: string): Promise<void>;
  appDiagnostics(): Promise<Diagnostics>;
  onImportProgress(cb: Listener): Promise<() => void>;
  pickM3uFile(): Promise<string | null>;
}

function tauriBackend(): Backend {
  // Dynamische Importe, damit der Browser-Build ohne Tauri-Runtime lädt.
  const inv = async <T,>(cmd: string, args?: Record<string, unknown>) => {
    const { invoke } = await import("@tauri-apps/api/core");
    return invoke<T>(cmd, args);
  };
  return {
    listProviders: () => inv("list_providers"),
    addProvider: (input) => inv("add_provider", { input }),
    deleteProvider: (id) => inv("delete_provider", { id }),
    importM3uFromUrl: (providerId, url) => inv("import_m3u_from_url", { providerId, url }),
    importM3uFromFile: (providerId, path) => inv("import_m3u_from_file", { providerId, path }),
    listGroups: (providerId) => inv("list_groups", { providerId }),
    listChannels: (providerId, groupId, limit, offset) =>
      inv("list_channels", { providerId, groupId, limit, offset }),
    searchChannels: (query) => inv("search_channels", { query }),
    getSetting: (key) => inv("get_setting", { key }),
    setSetting: (key, value) => inv("set_setting", { key, value }),
    appDiagnostics: () => inv("app_diagnostics"),
    onImportProgress: async (cb) => {
      const { listen } = await import("@tauri-apps/api/event");
      const un = await listen("import-progress", (e) => cb(e.payload));
      return () => un();
    },
    pickM3uFile: async () => {
      const { open } = await import("@tauri-apps/plugin-dialog");
      const sel = await open({
        multiple: false,
        filters: [{ name: "Playlisten", extensions: ["m3u", "m3u8"] }],
      });
      return typeof sel === "string" ? sel : null;
    },
  };
}

/** Browser-Mock für die UI-Entwicklung (nicht persistent). */
function mockBackend(): Backend {
  let nextId = 1;
  const providers: Provider[] = [];
  const groups = new Map<number, ChannelGroup[]>();
  const channels = new Map<number, Channel[]>();
  const settings = new Map<string, string>();
  const listeners: Listener[] = [];

  const emit = (p: unknown) => listeners.forEach((l) => l(p));
  const demoImport = (pid: number): ImportReport => {
    const gs: ChannelGroup[] = ["Nachrichten", "Unterhaltung", "Sport", "Doku"].map((name, i) => ({
      id: i + 1, provider_id: pid, name, sort_index: i, hidden: false,
    }));
    const cs: Channel[] = Array.from({ length: 240 }, (_, i) => ({
      id: i + 1, provider_id: pid, group_id: (i % 4) + 1,
      name: `Demo-Sender ${String(i + 1).padStart(3, "0")}`,
      url: `https://demo.invalid/stream/${i + 1}.m3u8`,
      tvg_id: `demo${i + 1}`, tvg_name: null, logo_url: null,
      channel_number: i + 1, is_radio: false, hidden: false, locked: false, sort_index: i,
    }));
    groups.set(pid, gs);
    channels.set(pid, cs);
    return { total_lines: 481, channels_parsed: 240, channels_skipped: 3, groups_found: 4, warnings: [], encoding: "UTF-8" };
  };

  return {
    listProviders: async () => [...providers],
    addProvider: async (input) => {
      const id = nextId++;
      const now = Math.floor(Date.now() / 1000);
      providers.push({
        id, name: input.name, kind: input.kind, source: input.source,
        username: input.username ?? null, secret_ref: null, enabled: true,
        auto_refresh_hours: null, epg_url: null, user_agent: null, referer: null,
        last_refresh_at: null, expires_at: null, max_connections: null,
        created_at: now, updated_at: now,
      });
      return id;
    },
    deleteProvider: async (id) => {
      const i = providers.findIndex((p) => p.id === id);
      if (i >= 0) providers.splice(i, 1);
      groups.delete(id); channels.delete(id);
    },
    importM3uFromUrl: async (pid) => {
      emit({ provider_id: pid, stage: "laden", channels: 0 });
      await new Promise((r) => setTimeout(r, 500));
      emit({ provider_id: pid, stage: "verarbeiten", channels: 0 });
      await new Promise((r) => setTimeout(r, 400));
      const rep = demoImport(pid);
      emit({ provider_id: pid, stage: "speichern", channels: rep.channels_parsed });
      await new Promise((r) => setTimeout(r, 300));
      emit({ provider_id: pid, stage: "fertig", channels: rep.channels_parsed });
      const p = providers.find((x) => x.id === pid);
      if (p) p.last_refresh_at = Math.floor(Date.now() / 1000);
      return rep;
    },
    importM3uFromFile: async function (pid) { return this.importM3uFromUrl(pid, ""); },
    listGroups: async (pid) => groups.get(pid) ?? [],
    listChannels: async (pid, gid, limit, offset) =>
      (channels.get(pid) ?? [])
        .filter((c) => gid == null || c.group_id === gid)
        .slice(offset, offset + limit),
    searchChannels: async (q) => {
      const needle = q.toLowerCase();
      return [...channels.values()].flat().filter((c) => c.name.toLowerCase().includes(needle)).slice(0, 100);
    },
    getSetting: async (k) => settings.get(k) ?? null,
    setSetting: async (k, v) => { settings.set(k, v); },
    appDiagnostics: async () => ({
      app_version: "0.1.0 (Browser-Vorschau)", os: "browser", arch: "-", db_schema_version: 1,
    }),
    onImportProgress: async (cb) => { listeners.push(cb); return () => {
      const i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1);
    }; },
    pickM3uFile: async () => window.prompt("Pfad zur M3U-Datei (Browser-Vorschau):") ?? null,
  };
}

export const backend: Backend = isTauri ? tauriBackend() : mockBackend();
