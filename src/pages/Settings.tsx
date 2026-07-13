import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { backend } from "../lib/backend";
import type { Diagnostics } from "../lib/types";

export default function Settings() {
  const { t, i18n } = useTranslation();
  const [diag, setDiag] = useState<Diagnostics | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    backend.appDiagnostics().then(setDiag).catch(() => setDiag(null));
    backend.getSetting("ui.language").then((v) => { if (v) void i18n.changeLanguage(v); });
  }, [i18n]);

  const setLang = async (lang: string) => {
    await i18n.changeLanguage(lang);
    await backend.setSetting("ui.language", lang);
    setSaved(true);
    window.setTimeout(() => setSaved(false), 1500);
  };

  return (
    <>
      <h1>{t("settings.title")}</h1>

      <section className="card" style={{ display: "grid", gap: 12, maxWidth: 560 }}>
        <h2>{t("settings.general")}</h2>
        <label className="row" style={{ justifyContent: "space-between" }}>
          <span>{t("settings.language")}</span>
          <select
            style={{ width: 200 }}
            value={i18n.language.startsWith("en") ? "en" : "de"}
            onChange={(e) => void setLang(e.target.value)}
          >
            <option value="de">Deutsch</option>
            <option value="en">English</option>
          </select>
        </label>
        {saved && <p className="faint" role="status">{t("settings.saved")}</p>}
      </section>

      <section className="card" style={{ display: "grid", gap: 8, maxWidth: 560 }}>
        <h2>{t("settings.diagnostics")}</h2>
        {!diag && <div className="skeleton" style={{ height: 72 }} />}
        {diag && (
          <dl style={{ margin: 0, display: "grid", gridTemplateColumns: "auto 1fr", gap: "6px 18px" }}>
            <dt className="dim">{t("settings.appVersion")}</dt><dd style={{ margin: 0 }}>{diag.app_version}</dd>
            <dt className="dim">{t("settings.os")}</dt><dd style={{ margin: 0 }}>{diag.os} ({diag.arch})</dd>
            <dt className="dim">{t("settings.dbVersion")}</dt><dd style={{ margin: 0 }}>v{diag.db_schema_version}</dd>
          </dl>
        )}
      </section>
    </>
  );
}
