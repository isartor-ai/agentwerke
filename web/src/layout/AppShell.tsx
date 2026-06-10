import { NavLink, Outlet } from 'react-router-dom';
import type { AuthState } from '../types';
import { LoadingState } from '../components/LoadingState';

interface AppShellProps {
  auth: AuthState;
}

const navItems = [
  { label: 'Runs', path: '/runs' },
  { label: 'Workflows', path: '/workflows' },
  { label: 'Approvals', path: '/approvals' },
  { label: 'Policies', path: '/policies' },
  { label: 'Audit', path: '/audit' },
  { label: 'Integrations', path: '/integrations' },
  { label: 'Settings', path: '/settings' },
];

export function AppShell({ auth }: AppShellProps) {
  const user = auth.user;

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <aside className="sidebar" aria-label="Sidebar">
        <div className="sidebar-brand">
          <strong>Autofac</strong>
          <span>dev</span>
        </div>

        <nav aria-label="Primary navigation">
          <ul role="list" className="nav-list">
            {navItems.map((item) => (
              <li key={item.path}>
                <NavLink
                  to={item.path}
                  className={({ isActive }) => (isActive ? 'nav-link nav-link-active' : 'nav-link')}
                >
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>

        <footer className="sidebar-footer">
          {auth.status === 'loading' ? (
            <LoadingState message="Loading user" className="compact-loading" />
          ) : user ? (
            <div>
              <div className="avatar">{user.avatarInitials}</div>
              <p className="sidebar-user-name">{user.name}</p>
              <p className="sidebar-user-role">{user.role}</p>
            </div>
          ) : (
            <NavLink to="/login" className="btn btn-secondary btn-block">
              Sign in
            </NavLink>
          )}
        </footer>
      </aside>

      <div className="content-wrap">
        <header className="top-bar">
          <label htmlFor="global-search" className="sr-only">
            Search workflows and runs
          </label>
          <input id="global-search" type="search" placeholder="Search workflows, runs" />
          <div className="top-user">{user?.email ?? 'Guest'}</div>
        </header>

        <main id="main-content" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
