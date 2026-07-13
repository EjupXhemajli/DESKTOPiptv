import { Navigate, Route, Routes } from "react-router-dom";
import Sidebar from "./components/Sidebar";
import Home from "./pages/Home";
import LiveTV from "./pages/LiveTV";
import Providers from "./pages/Providers";
import Search from "./pages/Search";
import Settings from "./pages/Settings";
import PhasePage from "./pages/PhasePage";
import { useTranslation } from "react-i18next";

export default function App() {
  const { t } = useTranslation();
  return (
    <div className="app-shell">
      <Sidebar />
      <main className="app-main">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/live" element={<LiveTV />} />
          <Route path="/guide" element={<PhasePage title={t("nav.guide")} phase={6} emptyKey="empty.guide" />} />
          <Route path="/movies" element={<PhasePage title={t("nav.movies")} phase={5} emptyKey="empty.movies" />} />
          <Route path="/series" element={<PhasePage title={t("nav.series")} phase={5} emptyKey="empty.series" />} />
          <Route path="/favorites" element={<PhasePage title={t("nav.favorites")} phase={8} emptyKey="empty.favorites" />} />
          <Route path="/recordings" element={<PhasePage title={t("nav.recordings")} phase={9} emptyKey="empty.recordings" />} />
          <Route path="/history" element={<PhasePage title={t("nav.history")} phase={8} emptyKey="empty.history" />} />
          <Route path="/search" element={<Search />} />
          <Route path="/providers" element={<Providers />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
}
