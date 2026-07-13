//! EXIPTV Tauri-Shell.
//!
//! Verantwortlich für: Fenster, IPC-Commands, Logging (rotierend, maskiert),
//! Datenbank-Lebenszyklus, sichere Zugangsdaten (Windows Credential Manager
//! über `keyring`) und HTTP-Import.

mod commands;
mod http;
mod secrets;
mod state;

use state::AppState;
use tauri::Manager;

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            init_logging(app)?;
            let state = AppState::init(app.handle())?;
            app.manage(state);
            tracing::info!("EXIPTV gestartet");
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::list_providers,
            commands::add_provider,
            commands::delete_provider,
            commands::set_provider_enabled,
            commands::import_m3u_from_file,
            commands::import_m3u_from_url,
            commands::list_groups,
            commands::list_channels,
            commands::search_channels,
            commands::get_setting,
            commands::set_setting,
            commands::app_diagnostics,
        ])
        .run(tauri::generate_context!())
        .expect("EXIPTV konnte nicht gestartet werden");
}

/// Rotierendes Datei-Logging unter %APPDATA%/EXIPTV/logs.
/// Zugangsdaten werden bereits an der Quelle maskiert
/// (`exiptv_core::util::sanitize`); zusätzlich schreiben wir nie
/// Roh-URLs von Anbietern ins Log.
fn init_logging(app: &tauri::App) -> Result<(), Box<dyn std::error::Error>> {
    let log_dir = app.path().app_log_dir()?;
    std::fs::create_dir_all(&log_dir)?;
    let appender = tracing_appender::rolling::daily(&log_dir, "exiptv.log");
    let (writer, guard) = tracing_appender::non_blocking(appender);
    // Guard muss App-Lebensdauer haben:
    Box::leak(Box::new(guard));
    tracing_subscriber::fmt()
        .with_writer(writer)
        .with_ansi(false)
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "info".into()),
        )
        .init();
    // Alte Logdateien begrenzen (max. 14 Tage).
    prune_old_logs(&log_dir, 14);
    Ok(())
}

fn prune_old_logs(dir: &std::path::Path, keep_days: u64) {
    let Ok(entries) = std::fs::read_dir(dir) else { return };
    let cutoff = std::time::SystemTime::now()
        - std::time::Duration::from_secs(keep_days * 24 * 3600);
    for e in entries.flatten() {
        if let Ok(meta) = e.metadata() {
            if meta.is_file() && meta.modified().map(|m| m < cutoff).unwrap_or(false) {
                let _ = std::fs::remove_file(e.path());
            }
        }
    }
}
