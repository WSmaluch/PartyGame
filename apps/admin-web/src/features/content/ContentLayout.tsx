import { Link, Outlet } from 'react-router-dom';

export function ContentLayout() {
  return <main className="content-shell">
    <nav aria-label="Nawigacja treści"><Link to="/admin">Panel</Link><span>/</span><Link to="/admin/content">Pakiety treści</Link></nav>
    <Outlet />
  </main>;
}
