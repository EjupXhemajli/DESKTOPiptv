import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { backend } from "../lib/backend";
import EmptyState from "../components/EmptyState";
import type { ImportProgress, ImportReport, Provider, ProviderKind } from "../lib/types";

interface FormState {
  name: string;
  kind: ProviderKind;
  source: string;
  username: string;
  password: string;
}

const EMPTY_FORM: FormState = { name: "", kind: "m3u_url", source: "", username: "", password: "" };

export default function Providers() {
  const { t } = useTranslation();
  const [providers, setProviders] = useState<Provider[] | null>(null);
  const [counts, setCounts] = useState<Record<number, number>>({});
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);
  const [progress, setProgress] = useState<Record<number, ImportProgress>>({});
  const [reports, setReports] = useState<Record<number, ImportReport>>({});
  const [errors, setErrors] = useState<Record<number, string>>({});

  const reload = useCallback(async () => {
    const ps = await backend.listProviders();
    setProviders(ps);
    const c: Record<number, number> = {};
    for (const p of ps) {
      // Kanalanzahl über die erste Seite ermitteln wäre ungenau –
      // dafür gibt es das Import-Ergebnis; hier reicht die letzte Zählung.
      c[p.id] = counts[p.id] ?? 0;
    }
    setCounts(c);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => { void reload(); }, [reload]);

  useEffect(() => {
    let un: (() => void) | undefined;
    void backend.onImportProgress((raw) => {
      const p = raw as ImportProgress;
      setProgress((prev) => ({ ...prev, [p.provider_id]: p }));
    }).then((u) => { un = u; });
    return () => un?.();
  }, []);

  const submit = async () => {
    setFormError(null);
    if (!form.name.trim()) { setFormError(t("providers.validationName")); return; }
    if (!form.source.trim()) { setFormError(t("providers.validationSource")); return; }
    const id = await backend.addProvider({
      name: form.name.trim(),
      kind: form.kind,
      source: form.source.trim(),
      username: form.username.trim() || undefined,
      password: form.password || undefined,
    });
    setShowForm(false);
    setForm(EMPTY_FORM);
    await reload();
    await runImport(id, form.kind, form.source.trim());
  };

  const runImport = async (id: number, kind: ProviderKind, source: string) => {
    setErrors((e) => { const n = { ...e }; delete n[id]; return n; });
    try {
      const report =
        kind === "m3u_file"
          ? await backend.importM3uFromFile(id, source)
          : await backend.importM3uFromUrl(id, source);
      setReports((r) => ({ ...r, [id]: report }));
      setCounts((c) => ({ ...c, [id]: report.channels_parsed }));
      await reload();
    } catch (err) {
      setErrors((e) => ({ ...e, [id]: String(err) }));
    } finally {
      setProgress((p) => { const n = { ...p }; delete n[id]; return n; });
    }
  };

  const remove = async (id: number) => {
    if (!window.confirm(t("providers.deleteConfirm"))) return;
    await backend.deleteProvider(id);
    await reload();
  };

  const pickFile = async () => {
    const path = await backend.pickM3uFile();
    if (path) setForm((f) => ({ ...f, source: path }));
  };

  return (
    <>
      <header className="row" style={{ justifyContent: "space-between" }}>
        <h1>{t("providers.title")}</h1>
        <button className="primary" onClick={() => setShowForm((s) => !s)}>
          {showForm ? t("providers.cancel") : t("providers.add")}
        </button>
      </header>

      {showForm && (
        <section className="card" style={{ display: "grid", gap: 12, maxWidth: 640 }}>
          <label>
            <span className="dim">{t("providers.name")}</span>
            <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </label>
          <label>
            <span className="dim">{t("providers.type")}</span>
            <select
              value={form.kind}
              onChange={(e) => setForm({ ...form, kind: e.target.value as ProviderKind, source: "" })}
            >
              <option value="m3u_url">{t("providers.typeM3uUrl")}</option>
              <option value="m3u_file">{t("providers.typeM3uFile")}</option>
              <option value="xtream">{t("providers.typeXtream")}</option>
            </select>
          </label>
          <label>
            <span className="dim">{t("providers.source")}</span>
            <div className="row">
              <input
                className="grow"
                value={form.source}
                onChange={(e) => setForm({ ...form, source: e.target.value })}
                placeholder={
                  form.kind === "m3u_url" ? t("providers.sourceUrlPlaceholder")
                  : form.kind === "m3u_file" ? t("providers.sourceFilePlaceholder")
                  : t("providers.serverPlaceholder")
                }
              />
              {form.kind === "m3u_file" && (
                <button onClick={() => void pickFile()}>{t("providers.chooseFile")}</button>
              )}
            </div>
          </label>
          {form.kind === "xtream" && (
            <>
              <label>
                <span className="dim">{t("providers.username")}</span>
                <input value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} autoComplete="off" />
              </label>
              <label>
                <span className="dim">{t("providers.password")}</span>
                <input type="password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} autoComplete="new-password" />
                <p className="faint">{t("providers.passwordHint")}</p>
              </label>
            </>
          )}
          {formError && <p role="alert" style={{ color: "var(--danger)" }}>{formError}</p>}
          <div className="row">
            <button className="primary" onClick={() => void submit()}>{t("providers.save")}</button>
            <button onClick={() => { setShowForm(false); setFormError(null); }}>{t("providers.cancel")}</button>
          </div>
        </section>
      )}

      {providers === null && <div className="skeleton" style={{ height: 96 }} />}

      {providers !== null && providers.length === 0 && !showForm && (
        <EmptyState
          title={t("providers.emptyTitle")}
          text={t("providers.emptyText")}
          action={<button className="primary" onClick={() => setShowForm(true)}>{t("providers.add")}</button>}
        />
      )}

      {providers?.map((p) => {
        const prog = progress[p.id];
        const rep = reports[p.id];
        const err = errors[p.id];
        return (
          <section key={p.id} className="card" style={{ display: "grid", gap: 8 }}>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <div>
                <h2>{p.name}</h2>
                <p className="faint">
                  {kindLabel(p.kind, t)} ·{" "}
                  {t("providers.lastRefresh", {
                    time: p.last_refresh_at
                      ? new Date(p.last_refresh_at * 1000).toLocaleString()
                      : t("providers.never"),
                  })}
                </p>
              </div>
              <div className="row">
                <button onClick={() => void runImport(p.id, p.kind, p.source)} disabled={!!prog}>
                  {t("providers.refresh")}
                </button>
                <button className="danger" onClick={() => void remove(p.id)} disabled={!!prog}>
                  {t("providers.delete")}
                </button>
              </div>
            </div>

            {prog && (
              <p className="dim" role="status">
                {prog.stage === "laden" && t("providers.importStageLaden")}
                {prog.stage === "verarbeiten" && t("providers.importStageVerarbeiten")}
                {prog.stage === "speichern" && t("providers.importStageSpeichern", { count: prog.channels })}
              </p>
            )}
            {rep && !prog && (
              <p className="dim">
                {t("providers.importDone", { count: rep.channels_parsed })}
                {rep.channels_skipped > 0 && <> · {t("providers.importSkipped", { count: rep.channels_skipped })}</>}
                {rep.encoding && <span className="faint"> · {rep.encoding}</span>}
              </p>
            )}
            {err && <p role="alert" style={{ color: "var(--danger)" }}>{err}</p>}
          </section>
        );
      })}
    </>
  );
}

function kindLabel(kind: ProviderKind, t: (k: string) => string): string {
  switch (kind) {
    case "m3u_url": return t("providers.typeM3uUrl");
    case "m3u_file": return t("providers.typeM3uFile");
    case "xtream": return t("providers.typeXtream");
    default: return kind;
  }
}
