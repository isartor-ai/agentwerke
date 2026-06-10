import { useEffect, useState } from 'react';
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { AppShell } from './layout/AppShell';
import type { AuthState } from './types';
import { ApprovalsDashboard } from './views/ApprovalsDashboard';
import { Login } from './views/Login';
import { NotFound } from './views/NotFound';
import { Placeholder } from './views/Placeholder';
import { RunBoard } from './views/RunBoard';
import { RunDetail } from './views/RunDetail';
import { WorkflowDesigner } from './views/WorkflowDesigner';

const mockUser = {
  id: 'user-1',
  name: 'Alex Engineer',
  email: 'alex.engineer@example.com',
  role: 'Workflow Engineer',
  avatarInitials: 'AE',
};

function ProtectedRoutes({ auth }: { auth: AuthState }) {
  if (auth.status === 'unauthenticated') {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
}

export default function App() {
  const [auth, setAuth] = useState<AuthState>({ status: 'loading' });

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setAuth({ status: 'authenticated', user: mockUser });
    }, 450);

    return () => {
      window.clearTimeout(timer);
    };
  }, []);

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />

        <Route element={<ProtectedRoutes auth={auth} />}>
          <Route element={<AppShell auth={auth} />}>
            <Route path="/" element={<Navigate to="/runs" replace />} />
            <Route path="/runs" element={<RunBoard />} />
            <Route path="/runs/:runId" element={<RunDetail />} />
            <Route path="/workflows" element={<WorkflowDesigner />} />
            <Route path="/approvals" element={<ApprovalsDashboard />} />
            <Route
              path="/policies"
              element={<Placeholder title="Policies" description="Policy authoring and simulation workflow." />}
            />
            <Route
              path="/audit"
              element={<Placeholder title="Audit" description="Immutable audit events and decision trace explorer." />}
            />
            <Route
              path="/integrations"
              element={<Placeholder title="Integrations" description="External connectors and webhook configuration." />}
            />
            <Route
              path="/settings"
              element={<Placeholder title="Settings" description="Tenant, access control, and platform settings." />}
            />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
