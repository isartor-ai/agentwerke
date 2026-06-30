import { type FormEvent, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { roleLabel } from '../auth/permissions';
import type { AuthState } from '../types';
import { LoadingState } from '../components/LoadingState';

interface AppShellProps {
  auth: AuthState;
}

const navItems = [
  { label: 'Runs', path: '/runs' },
  { label: 'Workflows', path: '/workflows' },
  { label: 'Agents', path: '/agents' },
  { label: 'Approvals', path: '/approvals' },
  { label: 'Policies', path: '/policies' },
  { label: 'Audit', path: '/audit' },
  { label: 'Integrations', path: '/integrations' },
  { label: 'Settings', path: '/settings' },
];

const enterpriseSignals = [
  { label: 'Tenant', value: 'Acme Platform' },
  { label: 'Region', value: 'EU-West' },
  { label: 'SSO', value: 'Enforced' },
];

export function AppShell({ auth }: AppShellProps) {
  const user = auth.user;
  const navigate = useNavigate();
  const [globalQuery, setGlobalQuery] = useState('');

  const handleSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const query = globalQuery.trim();
    const search = query ? `?q=${encodeURIComponent(query)}` : '';
    navigate(`/runs${search}`);
  };

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <aside className="sidebar" aria-label="Sidebar">
        <div className="sidebar-brand">
          <div>
            <strong>Autofac</strong>
            <span>v2.4.0-stable</span>
          </div>
        </div>

        <NavLink to="/workflows" className="sidebar-cta">
          Deploy Workflow
        </NavLink>

        <section className="sidebar-readiness" aria-label="Enterprise readiness posture">
          <div>
            <span className="status-dot healthy" aria-hidden="true" />
            <strong>Prod control plane</strong>
          </div>
          <dl>
            {enterpriseSignals.map((signal) => (
              <div key={signal.label}>
                <dt>{signal.label}</dt>
                <dd>{signal.value}</dd>
              </div>
            ))}
          </dl>
        </section>

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
              <p className="sidebar-user-role">{roleLabel(user.roles)}</p>
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
          <form className="top-search" role="search" onSubmit={handleSearch}>
            <label htmlFor="global-search" className="sr-only">
              Search workflows and runs
            </label>
            <input
              id="global-search"
              type="search"
              value={globalQuery}
              placeholder="Search runs, approvals, evidence, agents..."
              onChange={(event) => setGlobalQuery(event.target.value)}
            />
            <button type="submit" className="btn btn-secondary top-search-submit">
              Search
            </button>
          </form>
          <div className="top-context" aria-label="Environment context">
            <span className="mini-badge healthy">PROD</span>
            <span className="mini-badge neutral">EU-WEST</span>
            <span className="mini-badge neutral">AUDIT ON</span>
          </div>
          <div className="top-operator">
            <span className="operator-pulse" aria-hidden="true" />
            <span className="avatar top-avatar">{user?.avatarInitials ?? 'GU'}</span>
            <span className="top-user">{user?.email ?? 'Guest'}</span>
          </div>
        </header>

        <nav className="mobile-nav" aria-label="Primary navigation compact">
          {navItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              className={({ isActive }) => (isActive ? 'mobile-nav-link mobile-nav-link-active' : 'mobile-nav-link')}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>

        <main id="main-content" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
