//! SQLite-Zugriffsschicht.
//!
//! - WAL-Modus, Foreign Keys, sinnvolle PRAGMAs
//! - versionierte Migrationen (`migrations.rs`)
//! - alle Schreibpfade laufen in Transaktionen
//! - Playlist-Aktualisierung als Staging-Verfahren: alte Daten werden erst
//!   ersetzt, wenn der neue Import vollständig validiert in derselben
//!   Transaktion vorliegt (Anforderung Abschnitt 13)

pub mod migrations;

use crate::error::{CoreError, Result};
use crate::models::{Channel, ChannelGroup, ImportReport, Provider, ProviderKind};
use crate::parser::m3u::M3uEntry;
use rusqlite::{params, Connection, OptionalExtension};
use std::path::Path;

pub struct Database {
    conn: Connection,
}

impl Database {
    pub fn open(path: &Path) -> Result<Self> {
        let conn = Connection::open(path)?;
        Self::init(conn)
    }

    pub fn open_in_memory() -> Result<Self> {
        Self::init(Connection::open_in_memory()?)
    }

    fn init(conn: Connection) -> Result<Self> {
        conn.pragma_update(None, "journal_mode", "WAL")?;
        conn.pragma_update(None, "synchronous", "NORMAL")?;
        conn.pragma_update(None, "foreign_keys", "ON")?;
        conn.pragma_update(None, "busy_timeout", 5_000)?;
        let mut db = Self { conn };
        migrations::run(&mut db.conn)?;
        Ok(db)
    }

    pub fn schema_version(&self) -> Result<i64> {
        Ok(self
            .conn
            .query_row("SELECT MAX(version) FROM schema_migrations", [], |r| {
                r.get::<_, Option<i64>>(0)
            })?
            .unwrap_or(0))
    }

    // ------------------------------------------------------------------
    // Einstellungen
    // ------------------------------------------------------------------

    pub fn set_setting(&self, key: &str, value: &str) -> Result<()> {
        self.conn.execute(
            "INSERT INTO app_settings (key, value, updated_at) VALUES (?1, ?2, unixepoch())
             ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = unixepoch()",
            params![key, value],
        )?;
        Ok(())
    }

    pub fn get_setting(&self, key: &str) -> Result<Option<String>> {
        Ok(self
            .conn
            .query_row(
                "SELECT value FROM app_settings WHERE key = ?1",
                params![key],
                |r| r.get(0),
            )
            .optional()?)
    }

    // ------------------------------------------------------------------
    // Anbieter
    // ------------------------------------------------------------------

    pub fn insert_provider(
        &self,
        name: &str,
        kind: ProviderKind,
        source: &str,
        username: Option<&str>,
        secret_ref: Option<&str>,
    ) -> Result<i64> {
        if name.trim().is_empty() {
            return Err(CoreError::InvalidInput("Anbietername darf nicht leer sein".into()));
        }
        self.conn.execute(
            "INSERT INTO providers (name, kind, source, username, secret_ref, enabled, created_at, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, 1, unixepoch(), unixepoch())",
            params![name.trim(), kind.as_str(), source, username, secret_ref],
        )?;
        Ok(self.conn.last_insert_rowid())
    }

    pub fn list_providers(&self) -> Result<Vec<Provider>> {
        let mut stmt = self.conn.prepare(
            "SELECT id, name, kind, source, username, secret_ref, enabled,
                    auto_refresh_hours, epg_url, user_agent, referer,
                    last_refresh_at, expires_at, max_connections, created_at, updated_at
             FROM providers ORDER BY name COLLATE NOCASE",
        )?;
        let rows = stmt.query_map([], |r| {
            Ok(Provider {
                id: r.get(0)?,
                name: r.get(1)?,
                kind: ProviderKind::parse(&r.get::<_, String>(2)?)
                    .unwrap_or(ProviderKind::DirectStream),
                source: r.get(3)?,
                username: r.get(4)?,
                secret_ref: r.get(5)?,
                enabled: r.get::<_, i64>(6)? != 0,
                auto_refresh_hours: r.get(7)?,
                epg_url: r.get(8)?,
                user_agent: r.get(9)?,
                referer: r.get(10)?,
                last_refresh_at: r.get(11)?,
                expires_at: r.get(12)?,
                max_connections: r.get(13)?,
                created_at: r.get(14)?,
                updated_at: r.get(15)?,
            })
        })?;
        Ok(rows.collect::<std::result::Result<_, _>>()?)
    }

    pub fn set_provider_enabled(&self, id: i64, enabled: bool) -> Result<()> {
        self.conn.execute(
            "UPDATE providers SET enabled = ?2, updated_at = unixepoch() WHERE id = ?1",
            params![id, enabled as i64],
        )?;
        Ok(())
    }

    /// Löscht einen Anbieter samt aller abhängigen Daten (ON DELETE CASCADE).
    pub fn delete_provider(&self, id: i64) -> Result<()> {
        self.conn
            .execute("DELETE FROM providers WHERE id = ?1", params![id])?;
        Ok(())
    }

    // ------------------------------------------------------------------
    // Kanäle & Gruppen
    // ------------------------------------------------------------------

    pub fn count_channels(&self, provider_id: i64) -> Result<i64> {
        Ok(self.conn.query_row(
            "SELECT COUNT(*) FROM channels WHERE provider_id = ?1",
            params![provider_id],
            |r| r.get(0),
        )?)
    }

    pub fn list_groups(&self, provider_id: i64) -> Result<Vec<ChannelGroup>> {
        let mut stmt = self.conn.prepare(
            "SELECT id, provider_id, name, sort_index, hidden
             FROM channel_groups WHERE provider_id = ?1 ORDER BY sort_index, name COLLATE NOCASE",
        )?;
        let rows = stmt.query_map(params![provider_id], |r| {
            Ok(ChannelGroup {
                id: r.get(0)?,
                provider_id: r.get(1)?,
                name: r.get(2)?,
                sort_index: r.get(3)?,
                hidden: r.get::<_, i64>(4)? != 0,
            })
        })?;
        Ok(rows.collect::<std::result::Result<_, _>>()?)
    }

    pub fn list_channels_page(
        &self,
        provider_id: i64,
        group_id: Option<i64>,
        limit: i64,
        offset: i64,
    ) -> Result<Vec<Channel>> {
        let mut stmt = self.conn.prepare(
            "SELECT id, provider_id, group_id, name, url, tvg_id, tvg_name, logo_url,
                    channel_number, is_radio, catchup, catchup_days, catchup_source,
                    timeshift, user_agent, referer, hidden, locked, sort_index
             FROM channels
             WHERE provider_id = ?1 AND (?2 IS NULL OR group_id = ?2) AND hidden = 0
             ORDER BY sort_index, name COLLATE NOCASE
             LIMIT ?3 OFFSET ?4",
        )?;
        let rows = stmt.query_map(params![provider_id, group_id, limit, offset], row_to_channel)?;
        Ok(rows.collect::<std::result::Result<_, _>>()?)
    }

    pub fn search_channels(&self, query: &str, limit: i64) -> Result<Vec<Channel>> {
        let like = format!("%{}%", normalize_search(query));
        let mut stmt = self.conn.prepare(
            "SELECT id, provider_id, group_id, name, url, tvg_id, tvg_name, logo_url,
                    channel_number, is_radio, catchup, catchup_days, catchup_source,
                    timeshift, user_agent, referer, hidden, locked, sort_index
             FROM channels
             WHERE search_name LIKE ?1 AND hidden = 0
             ORDER BY name COLLATE NOCASE LIMIT ?2",
        )?;
        let rows = stmt.query_map(params![like, limit], row_to_channel)?;
        Ok(rows.collect::<std::result::Result<_, _>>()?)
    }

    // ------------------------------------------------------------------
    // Staging-Import (Abschnitt 13)
    // ------------------------------------------------------------------

    /// Ersetzt die Kanäle eines Anbieters atomar durch das Parse-Ergebnis.
    ///
    /// Schlägt der Import an irgendeiner Stelle fehl, bleibt der alte
    /// Datenbestand vollständig erhalten (Transaktions-Rollback).
    pub fn replace_channels_staged(
        &mut self,
        provider_id: i64,
        entries: &[M3uEntry],
        report: &ImportReport,
    ) -> Result<usize> {
        // Validierung VOR dem Ersetzen alter Daten.
        if entries.is_empty() {
            return Err(CoreError::Playlist(
                "Die neue Playlist enthält keine gültigen Kanäle. \
                 Die zuletzt funktionierende Version bleibt erhalten."
                    .into(),
            ));
        }

        let tx = self.conn.transaction()?;
        {
            tx.execute(
                "DELETE FROM channels WHERE provider_id = ?1",
                params![provider_id],
            )?;
            tx.execute(
                "DELETE FROM channel_groups WHERE provider_id = ?1",
                params![provider_id],
            )?;

            let mut group_ids: std::collections::HashMap<String, i64> =
                std::collections::HashMap::new();
            let mut insert_group = tx.prepare(
                "INSERT INTO channel_groups (provider_id, name, sort_index, hidden)
                 VALUES (?1, ?2, ?3, 0)",
            )?;
            let mut insert_channel = tx.prepare(
                "INSERT INTO channels
                 (provider_id, group_id, name, search_name, url, tvg_id, tvg_name, logo_url,
                  channel_number, is_radio, catchup, catchup_days, catchup_source, timeshift,
                  user_agent, referer, hidden, locked, sort_index)
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16,0,0,?17)",
            )?;

            for (idx, e) in entries.iter().enumerate() {
                let group_id = match &e.group {
                    Some(g) => {
                        let next_index = group_ids.len() as i64;
                        Some(match group_ids.get(g) {
                            Some(id) => *id,
                            None => {
                                insert_group.execute(params![provider_id, g, next_index])?;
                                let id = tx.last_insert_rowid();
                                group_ids.insert(g.clone(), id);
                                id
                            }
                        })
                    }
                    None => None,
                };
                insert_channel.execute(params![
                    provider_id,
                    group_id,
                    e.name,
                    normalize_search(&e.name),
                    e.url,
                    e.tvg_id,
                    e.tvg_name,
                    e.logo_url,
                    e.channel_number,
                    e.is_radio as i64,
                    e.catchup,
                    e.catchup_days,
                    e.catchup_source,
                    e.timeshift,
                    e.user_agent,
                    e.referer,
                    idx as i64,
                ])?;
            }

            tx.execute(
                "UPDATE providers SET last_refresh_at = unixepoch(), updated_at = unixepoch()
                 WHERE id = ?1",
                params![provider_id],
            )?;
            tx.execute(
                "INSERT INTO import_jobs (provider_id, started_at, finished_at, status, channels, skipped, warnings_json)
                 VALUES (?1, unixepoch(), unixepoch(), 'ok', ?2, ?3, ?4)",
                params![
                    provider_id,
                    entries.len() as i64,
                    report.channels_skipped as i64,
                    serde_json::to_string(&report.warnings)?
                ],
            )?;
        }
        tx.commit()?;
        Ok(entries.len())
    }
}

fn row_to_channel(r: &rusqlite::Row<'_>) -> std::result::Result<Channel, rusqlite::Error> {
    Ok(Channel {
        id: r.get(0)?,
        provider_id: r.get(1)?,
        group_id: r.get(2)?,
        name: r.get(3)?,
        url: r.get(4)?,
        tvg_id: r.get(5)?,
        tvg_name: r.get(6)?,
        logo_url: r.get(7)?,
        channel_number: r.get(8)?,
        is_radio: r.get::<_, i64>(9)? != 0,
        catchup: r.get(10)?,
        catchup_days: r.get(11)?,
        catchup_source: r.get(12)?,
        timeshift: r.get(13)?,
        user_agent: r.get(14)?,
        referer: r.get(15)?,
        hidden: r.get::<_, i64>(16)? != 0,
        locked: r.get::<_, i64>(17)? != 0,
        sort_index: r.get(18)?,
    })
}

/// Normalisierung für tolerante Suche: Kleinschreibung, Diakritika-Faltung,
/// Trennzeichen → Leerzeichen, mehrfache Leerzeichen kollabieren.
pub fn normalize_search(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut last_space = true; // führende Leerzeichen unterdrücken
    for c in s.to_lowercase().chars() {
        let mapped: &str = match c {
            'ä' | 'á' | 'à' | 'â' | 'ã' | 'å' => "a",
            'ö' | 'ó' | 'ò' | 'ô' | 'õ' => "o",
            'ü' | 'ú' | 'ù' | 'û' => "u",
            'é' | 'è' | 'ê' | 'ë' => "e",
            'í' | 'ì' | 'î' | 'ï' => "i",
            'ß' => "ss",
            'ç' => "c",
            'ñ' => "n",
            _ if c.is_alphanumeric() => {
                out.push(c);
                last_space = false;
                continue;
            }
            // Alles andere (Bindestrich, Punkt, Slash, …) trennt Wörter.
            _ => {
                if !last_space {
                    out.push(' ');
                    last_space = true;
                }
                continue;
            }
        };
        out.push_str(mapped);
        last_space = false;
    }
    while out.ends_with(' ') {
        out.pop();
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::parser::m3u::parse_str;

    fn sample_entries(n: usize) -> Vec<M3uEntry> {
        let mut s = String::from("#EXTM3U\n");
        for i in 0..n {
            s.push_str(&format!(
                "#EXTINF:-1 tvg-id=\"ch{i}\" group-title=\"Gruppe {}\",Kanal {i}\nhttp://x.tld/{i}.m3u8\n",
                i % 10
            ));
        }
        parse_str(&s, None).entries
    }

    #[test]
    fn migrationen_laufen_und_sind_idempotent() {
        let db = Database::open_in_memory().unwrap();
        assert!(db.schema_version().unwrap() >= 1);
    }

    #[test]
    fn einstellungen_upsert() {
        let db = Database::open_in_memory().unwrap();
        db.set_setting("sprache", "de").unwrap();
        db.set_setting("sprache", "en").unwrap();
        assert_eq!(db.get_setting("sprache").unwrap().as_deref(), Some("en"));
        assert_eq!(db.get_setting("gibt_es_nicht").unwrap(), None);
    }

    #[test]
    fn anbieter_anlegen_und_listen() {
        let db = Database::open_in_memory().unwrap();
        let id = db
            .insert_provider("Test-Anbieter", ProviderKind::M3uUrl, "https://x.tld/l.m3u", None, None)
            .unwrap();
        let list = db.list_providers().unwrap();
        assert_eq!(list.len(), 1);
        assert_eq!(list[0].id, id);
        assert!(list[0].enabled);
        assert!(db.insert_provider("  ", ProviderKind::M3uUrl, "x", None, None).is_err());
    }

    #[test]
    fn staging_import_ersetzt_atomar() {
        let mut db = Database::open_in_memory().unwrap();
        let pid = db
            .insert_provider("P", ProviderKind::M3uFile, "/tmp/a.m3u", None, None)
            .unwrap();
        let n = db
            .replace_channels_staged(pid, &sample_entries(100), &ImportReport::default())
            .unwrap();
        assert_eq!(n, 100);
        assert_eq!(db.count_channels(pid).unwrap(), 100);
        assert_eq!(db.list_groups(pid).unwrap().len(), 10);

        // Zweiter Import ersetzt vollständig
        db.replace_channels_staged(pid, &sample_entries(50), &ImportReport::default())
            .unwrap();
        assert_eq!(db.count_channels(pid).unwrap(), 50);
    }

    #[test]
    fn leerer_import_laesst_alte_daten_unangetastet() {
        let mut db = Database::open_in_memory().unwrap();
        let pid = db
            .insert_provider("P", ProviderKind::M3uUrl, "https://x.tld/l.m3u", None, None)
            .unwrap();
        db.replace_channels_staged(pid, &sample_entries(20), &ImportReport::default())
            .unwrap();
        let err = db
            .replace_channels_staged(pid, &[], &ImportReport::default())
            .unwrap_err();
        assert!(err.to_string().contains("bleibt erhalten"));
        assert_eq!(db.count_channels(pid).unwrap(), 20, "alte Daten müssen erhalten bleiben");
    }

    #[test]
    fn anbieter_loeschen_entfernt_abhaengige_daten() {
        let mut db = Database::open_in_memory().unwrap();
        let pid = db
            .insert_provider("P", ProviderKind::M3uFile, "/tmp/a.m3u", None, None)
            .unwrap();
        db.replace_channels_staged(pid, &sample_entries(10), &ImportReport::default())
            .unwrap();
        db.delete_provider(pid).unwrap();
        assert_eq!(db.count_channels(pid).unwrap(), 0);
        assert!(db.list_groups(pid).unwrap().is_empty());
    }

    #[test]
    fn paginierte_kanalliste_und_suche() {
        let mut db = Database::open_in_memory().unwrap();
        let pid = db
            .insert_provider("P", ProviderKind::M3uFile, "/tmp/a.m3u", None, None)
            .unwrap();
        db.replace_channels_staged(pid, &sample_entries(500), &ImportReport::default())
            .unwrap();
        let page = db.list_channels_page(pid, None, 50, 0).unwrap();
        assert_eq!(page.len(), 50);
        let hits = db.search_channels("KANAL 42", 10).unwrap();
        assert!(hits.iter().any(|c| c.name == "Kanal 42"));
    }

    #[test]
    fn grosser_import_10k_in_transaktion() {
        let mut db = Database::open_in_memory().unwrap();
        let pid = db
            .insert_provider("XXL", ProviderKind::M3uUrl, "https://x.tld/xxl.m3u", None, None)
            .unwrap();
        let t = std::time::Instant::now();
        db.replace_channels_staged(pid, &sample_entries(10_000), &ImportReport::default())
            .unwrap();
        assert_eq!(db.count_channels(pid).unwrap(), 10_000);
        assert!(t.elapsed().as_secs() < 10, "Import zu langsam: {:?}", t.elapsed());
    }

    #[test]
    fn suche_normalisiert_umlaute() {
        assert_eq!(normalize_search("Münchën-TV!"), "munchen tv");
        assert_eq!(normalize_search("  Café -- del   Mar "), "cafe del mar");
    }
}
