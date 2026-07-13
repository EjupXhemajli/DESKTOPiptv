// Spiegel der Rust-Modelle (serde snake_case).
export type ProviderKind = "m3u_url" | "m3u_file" | "xtream" | "direct";

export interface Provider {
  id: number;
  name: string;
  kind: ProviderKind;
  source: string;
  username: string | null;
  secret_ref: string | null;
  enabled: boolean;
  auto_refresh_hours: number | null;
  epg_url: string | null;
  user_agent: string | null;
  referer: string | null;
  last_refresh_at: number | null;
  expires_at: number | null;
  max_connections: number | null;
  created_at: number;
  updated_at: number;
}

export interface ChannelGroup {
  id: number;
  provider_id: number;
  name: string;
  sort_index: number;
  hidden: boolean;
}

export interface Channel {
  id: number;
  provider_id: number;
  group_id: number | null;
  name: string;
  url: string;
  tvg_id: string | null;
  tvg_name: string | null;
  logo_url: string | null;
  channel_number: number | null;
  is_radio: boolean;
  hidden: boolean;
  locked: boolean;
  sort_index: number;
}

export interface ImportReport {
  total_lines: number;
  channels_parsed: number;
  channels_skipped: number;
  groups_found: number;
  warnings: string[];
  encoding: string | null;
}

export interface ImportProgress {
  provider_id: number;
  stage: "laden" | "verarbeiten" | "speichern" | "fertig";
  channels: number;
}

export interface Diagnostics {
  app_version: string;
  os: string;
  arch: string;
  db_schema_version: number;
}
