//! IPC-Commands: dünne Schicht über `exiptv-core`.
//!
//! Regeln:
//! - blockierende DB-Arbeit läuft über `spawn_blocking`, die UI friert nie ein
//! - Fehlermeldungen sind nutzerverständlich; technische Details gehen
//!   maskiert ins Log und in die Diagnoseansicht
//! - Zugangsdaten: nur `secret_ref` in der DB, Klartext in den
//!   Betriebssystem-Schlüsselbund

use crate::state::AppState;
use exiptv_core::models::{Channel, ChannelGroup, ImportReport, Provider, ProviderKind};
use exiptv_core::parser::m3u;
use exiptv_core::util::sanitize::sanitize_url;
use serde::{Deserialize, Serialize};
use tauri::{Emitter, State};

type CmdResult<T> = Result<T, String>;

#[derive(Serialize, Clone)]
pub struct ImportProgress {
    pub provider_id: i64,
    pub stage: String, // "laden" | "verarbeiten" | "speichern" | "fertig"
    pub channels: usize,
}

#[derive(Deserialize)]
pub struct NewProvider {
    pub name: String,
    pub kind: String,
    pub source: String,
    pub username: Option<String>,
    pub password: Option<String>,
}

#[tauri::command]
pub async fn list_providers(state: State<'_, AppState>) -> CmdResult<Vec<Provider>> {
    let db = state.db.lock().map_err(lock_err)?;
    db.list_providers().map_err(user_err)
}

#[tauri::command]
pub async fn add_provider(state: State<'_, AppState>, input: NewProvider) -> CmdResult<i64> {
    let kind = ProviderKind::parse(&input.kind)
        .ok_or_else(|| "Unbekannter Anbietertyp.".to_string())?;

    // Passwort in den Schlüsselbund, Referenz in die DB.
    let secret_ref = match &input.password {
        Some(pw) if !pw.is_empty() => {
            let reference = format!("provider:{}:{}", input.kind, uuid_like());
            crate::secrets::store(&reference, pw)?;
            Some(reference)
        }
        _ => None,
    };

    let db = state.db.lock().map_err(lock_err)?;
    db.insert_provider(
        &input.name,
        kind,
        &input.source,
        input.username.as_deref(),
        secret_ref.as_deref(),
    )
    .map_err(user_err)
}

#[tauri::command]
pub async fn delete_provider(state: State<'_, AppState>, id: i64) -> CmdResult<()> {
    // Erst Geheimnis-Referenz holen, dann DB-Eintrag löschen, dann Secret.
    let secret_ref = {
        let db = state.db.lock().map_err(lock_err)?;
        let provider = db
            .list_providers()
            .map_err(user_err)?
            .into_iter()
            .find(|p| p.id == id);
        db.delete_provider(id).map_err(user_err)?;
        provider.and_then(|p| p.secret_ref)
    };
    if let Some(r) = secret_ref {
        crate::secrets::delete(&r)?;
    }
    Ok(())
}

#[tauri::command]
pub async fn set_provider_enabled(
    state: State<'_, AppState>,
    id: i64,
    enabled: bool,
) -> CmdResult<()> {
    let db = state.db.lock().map_err(lock_err)?;
    db.set_provider_enabled(id, enabled).map_err(user_err)
}

#[tauri::command]
pub async fn import_m3u_from_file(
    app: tauri::AppHandle,
    state: State<'_, AppState>,
    provider_id: i64,
    path: String,
) -> CmdResult<ImportReport> {
    emit_progress(&app, provider_id, "laden", 0);
    let bytes = tokio::fs::read(&path)
        .await
        .map_err(|_| "Die Datei konnte nicht gelesen werden. Bitte Pfad und Berechtigungen prüfen.".to_string())?;
    import_bytes(&app, state, provider_id, bytes, None).await
}

#[tauri::command]
pub async fn import_m3u_from_url(
    app: tauri::AppHandle,
    state: State<'_, AppState>,
    provider_id: i64,
    url: String,
) -> CmdResult<ImportReport> {
    emit_progress(&app, provider_id, "laden", 0);
    let bytes = crate::http::get_with_retry(&state.http, &url, None, 2)
        .await
        .map_err(|e| {
            tracing::warn!(url = %sanitize_url(&url), fehler = %e, "Playlist-Download fehlgeschlagen");
            format!(
                "Die Playlist konnte nicht geladen werden ({e}). \
                 Die zuletzt funktionierende Version bleibt weiterhin verfügbar."
            )
        })?;
    import_bytes(&app, state, provider_id, bytes, Some(url)).await
}

async fn import_bytes(
    app: &tauri::AppHandle,
    state: State<'_, AppState>,
    provider_id: i64,
    bytes: Vec<u8>,
    base_url: Option<String>,
) -> CmdResult<ImportReport> {
    emit_progress(app, provider_id, "verarbeiten", 0);
    // Parsen + Speichern sind CPU-/IO-lastig → nicht auf dem UI-Thread.
    let app2 = app.clone();
    let result = tauri::async_runtime::spawn_blocking(move || {
        let parsed = m3u::parse_bytes(&bytes, base_url.as_deref());
        emit_progress(&app2, provider_id, "speichern", parsed.entries.len());
        (parsed.entries, parsed.report)
    })
    .await
    .map_err(|e| format!("Interner Fehler bei der Verarbeitung: {e}"))?;

    let (entries, mut report) = result;
    {
        let mut db = state.db.lock().map_err(lock_err)?;
        db.replace_channels_staged(provider_id, &entries, &report)
            .map_err(user_err)?;
    }
    report.channels_parsed = entries.len();
    emit_progress(app, provider_id, "fertig", entries.len());
    tracing::info!(
        provider_id,
        kanaele = entries.len(),
        uebersprungen = report.channels_skipped,
        "Playlist-Import abgeschlossen"
    );
    Ok(report)
}

#[tauri::command]
pub async fn list_groups(
    state: State<'_, AppState>,
    provider_id: i64,
) -> CmdResult<Vec<ChannelGroup>> {
    let db = state.db.lock().map_err(lock_err)?;
    db.list_groups(provider_id).map_err(user_err)
}

#[tauri::command]
pub async fn list_channels(
    state: State<'_, AppState>,
    provider_id: i64,
    group_id: Option<i64>,
    limit: i64,
    offset: i64,
) -> CmdResult<Vec<Channel>> {
    let db = state.db.lock().map_err(lock_err)?;
    db.list_channels_page(provider_id, group_id, limit.clamp(1, 500), offset.max(0))
        .map_err(user_err)
}

#[tauri::command]
pub async fn search_channels(state: State<'_, AppState>, query: String) -> CmdResult<Vec<Channel>> {
    if query.trim().len() < 2 {
        return Ok(vec![]);
    }
    let db = state.db.lock().map_err(lock_err)?;
    db.search_channels(&query, 100).map_err(user_err)
}

#[tauri::command]
pub async fn get_setting(state: State<'_, AppState>, key: String) -> CmdResult<Option<String>> {
    let db = state.db.lock().map_err(lock_err)?;
    db.get_setting(&key).map_err(user_err)
}

#[tauri::command]
pub async fn set_setting(state: State<'_, AppState>, key: String, value: String) -> CmdResult<()> {
    let db = state.db.lock().map_err(lock_err)?;
    db.set_setting(&key, &value).map_err(user_err)
}

#[derive(Serialize)]
pub struct Diagnostics {
    pub app_version: String,
    pub os: String,
    pub arch: String,
    pub db_schema_version: i64,
}

#[tauri::command]
pub async fn app_diagnostics(state: State<'_, AppState>) -> CmdResult<Diagnostics> {
    let db = state.db.lock().map_err(lock_err)?;
    Ok(Diagnostics {
        app_version: env!("CARGO_PKG_VERSION").into(),
        os: std::env::consts::OS.into(),
        arch: std::env::consts::ARCH.into(),
        db_schema_version: db.schema_version().map_err(user_err)?,
    })
}

// ----------------------------------------------------------------------

fn emit_progress(app: &tauri::AppHandle, provider_id: i64, stage: &str, channels: usize) {
    let _ = app.emit(
        "import-progress",
        ImportProgress { provider_id, stage: stage.into(), channels },
    );
}

fn lock_err<T>(_: T) -> String {
    "Interner Zustandsfehler. Bitte EXIPTV neu starten.".into()
}

fn user_err(e: exiptv_core::CoreError) -> String {
    tracing::error!(fehler = %e, "Core-Fehler");
    e.to_string()
}

/// Eindeutige Referenz ohne zusätzliche Abhängigkeit (Zeit + Zähler).
fn uuid_like() -> String {
    use std::sync::atomic::{AtomicU64, Ordering};
    static C: AtomicU64 = AtomicU64::new(0);
    format!(
        "{:x}-{:x}",
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0),
        C.fetch_add(1, Ordering::Relaxed)
    )
}
