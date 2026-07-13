import { useEffect, useMemo, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useTranslation } from "react-i18next";
import { backend } from "../lib/backend";
import EmptyState from "../components/EmptyState";
import { IconTv } from "../components/Icons";
import type { Channel, ChannelGroup, Provider } from "../lib/types";

const PAGE = 200;

export default function LiveTV() {
  const { t } = useTranslation();
  const [providers, setProviders] = useState<Provider[] | null>(null);
  const [providerId, setProviderId] = useState<number | null>(null);
  const [groups, setGroups] = useState<ChannelGroup[]>([]);
  const [groupId, setGroupId] = useState<number | null>(null);
  const [channels, setChannels] = useState<Channel[]>([]);
  const [exhausted, setExhausted] = useState(false);
  const listRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    backend.listProviders().then((ps) => {
      setProviders(ps);
      if (ps.length > 0) setProviderId(ps[0].id);
    });
  }, []);

  useEffect(() => {
    if (providerId == null) return;
    setChannels([]); setExhausted(false); setGroupId(null);
    backend.listGroups(providerId).then(setGroups);
  }, [providerId]);

  useEffect(() => {
    if (providerId == null) return;
    setChannels([]); setExhausted(false);
    void loadMore(providerId, groupId, []);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [providerId, groupId]);

  async function loadMore(pid: number, gid: number | null, current: Channel[]) {
    const page = await backend.listChannels(pid, gid, PAGE, current.length);
    setChannels([...current, ...page]);
    if (page.length < PAGE) setExhausted(true);
  }

  const virtualizer = useVirtualizer({
    count: channels.length,
    getScrollElement: () => listRef.current,
    estimateSize: () => 52,
    overscan: 12,
  });

  // Inkrementelles Nachladen am Listenende.
  const items = virtualizer.getVirtualItems();
  useEffect(() => {
    const last = items[items.length - 1];
    if (!last || exhausted || providerId == null) return;
    if (last.index >= channels.length - 20) {
      void loadMore(providerId, groupId, channels);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items, exhausted]);

  const groupName = useMemo(
    () => (groupId == null ? t("live.allChannels") : groups.find((g) => g.id === groupId)?.name ?? ""),
    [groupId, groups, t]
  );

  if (providers !== null && providers.length === 0) {
    return (
      <>
        <h1>{t("nav.live")}</h1>
        <EmptyState title={t("live.emptyTitle")} text={t("live.emptyText")} />
      </>
    );
  }

  return (
    <>
      <header className="row" style={{ justifyContent: "space-between" }}>
        <div>
          <h1>{t("nav.live")}</h1>
          <p className="dim">{groupName} · {t("live.channelCount", { count: channels.length })}</p>
        </div>
        {providers && providers.length > 1 && (
          <select
            style={{ width: 220 }}
            value={providerId ?? ""}
            onChange={(e) => setProviderId(Number(e.target.value))}
            aria-label={t("nav.providers")}
          >
            {providers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
        )}
      </header>

      <div className="row" style={{ alignItems: "stretch", gap: "var(--gap-m)", flex: 1, minHeight: 0 }}>
        {/* Gruppenliste */}
        <aside className="card" style={{ width: 240, overflowY: "auto", display: "flex", flexDirection: "column", gap: 2 }}>
          <GroupButton active={groupId === null} onClick={() => setGroupId(null)} label={t("live.allChannels")} />
          {groups.map((g) => (
            <GroupButton key={g.id} active={groupId === g.id} onClick={() => setGroupId(g.id)} label={g.name} />
          ))}
        </aside>

        {/* Virtualisierte Kanalliste */}
        <div ref={listRef} className="card grow" style={{ overflowY: "auto", padding: 8 }}>
          {channels.length === 0 && !exhausted && (
            <div style={{ display: "grid", gap: 8 }}>
              {Array.from({ length: 8 }).map((_, i) => <div key={i} className="skeleton" style={{ height: 44 }} />)}
            </div>
          )}
          <div style={{ height: virtualizer.getTotalSize(), position: "relative" }}>
            {items.map((vi) => {
              const c = channels[vi.index];
              if (!c) return null;
              return (
                <div
                  key={c.id}
                  className="row channel-row"
                  style={{
                    position: "absolute", top: 0, left: 0, right: 0,
                    transform: `translateY(${vi.start}px)`,
                    height: vi.size, padding: "0 10px",
                    borderRadius: "var(--radius-m)",
                  }}
                  tabIndex={0}
                  role="button"
                  aria-label={c.name}
                  title={t("live.playSoon")}
                >
                  <span className="faint" style={{ width: 44, textAlign: "right" }}>
                    {c.channel_number ?? vi.index + 1}
                  </span>
                  <span className="channel-logo" aria-hidden="true">
                    {c.logo_url
                      ? <img src={c.logo_url} alt="" loading="lazy"
                          onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
                      : <IconTv />}
                  </span>
                  <span className="grow" style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                    {c.name}
                  </span>
                  {c.is_radio && <span className="faint">Radio</span>}
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </>
  );
}

function GroupButton({ active, onClick, label }: { active: boolean; onClick: () => void; label: string }) {
  return (
    <button
      onClick={onClick}
      style={{
        textAlign: "left",
        background: active ? "linear-gradient(120deg, rgba(139,92,246,0.18), rgba(41,194,246,0.10))" : "transparent",
        borderColor: active ? "rgba(139,92,246,0.3)" : "transparent",
      }}
    >
      {label}
    </button>
  );
}
