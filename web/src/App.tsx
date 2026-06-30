import { useEffect, useState } from 'react';
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { apiClient } from './api/client';
import { LoadingState } from './components/LoadingState';
import { AppShell } from './layout/AppShell';
import type { AuthState, AuthUser } from './types';
import { ApprovalsDashboard } from './views/ApprovalsDashboard';
import { AgentRegistry } from './views/AgentRegistry';
import { Login } from './views/Login';
import { Integrations } from './views/Integrations';
import { NotFound } from './views/NotFound';
import { Placeholder } from './views/Placeholder';
import { Policies } from './views/Policies';
import { RunBoard } from './views/RunBoard';
import { RunDetail } from './views/RunDetail';
import { Settings } from './views/Settings';
import { WorkflowDesigner } from './views/WorkflowDesigner';

function ProtectedRoutes({ auth }: { auth: AuthState }) {
  if (auth.status === 'loading') {
    return <LoadingState message="Checking sign-in" />;
  }

  if (auth.status === 'unauthenticated') {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
}

export default function App() {
  const [auth, setAuth] = useState<AuthState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    apiClient
      .getCurrentUser()
      .then((user) => {
        if (!cancelled) {
          setAuth({ status: 'authenticated', user });
        }
      })
      .catch(() => {
        if (!cancelled) {
          setAuth({ status: 'unauthenticated' });
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const handleAuthenticated = (user: AuthUser) => {
    setAuth({ status: 'authenticated', user });
  };

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login auth={auth} onAuthenticated={handleAuthenticated} />} />

        <Route element={<ProtectedRoutes auth={auth} />}>
          <Route element={<AppShell auth={auth} />}>
            <Route path="/" element={<Navigate to="/runs" replace />} />
            <Route path="/runs" element={<RunBoard auth={auth} />} />
            <Route path="/runs/:runId" element={<RunDetail auth={auth} />} />
            <Route path="/workflows" element={<WorkflowDesigner auth={auth} />} />
            <Route path="/agents" element={<AgentRegistry auth={auth} />} />
            <Route path="/approvals" element={<ApprovalsDashboard auth={auth} />} />
            <Route
              path="/policies"
              element={<Policies auth={auth} />}
            />
            <Route
              path="/audit"
              element={<Placeholder title="Audit" description="Immutable audit events and decision trace explorer." />}
            />
            <Route
              path="/integrations"
              element={<Integrations />}
            />
            <Route
              path="/settings"
              element={<Settings auth={auth} />}
            />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
