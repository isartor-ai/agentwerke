import { type FormEvent, useEffect, useState } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { apiClient } from '../api/client';
import { AUTH_ROLES } from '../auth/permissions';
import type { AuthConfig, AuthRole, AuthState, AuthUser } from '../types';

interface LoginProps {
  auth: AuthState;
  onAuthenticated: (user: AuthUser) => void;
}

export function Login({ auth, onAuthenticated }: LoginProps) {
  const navigate = useNavigate();
  const [config, setConfig] = useState<AuthConfig | null>(null);
  const [selectedRole, setSelectedRole] = useState<AuthRole>('Viewer');
  const [subject, setSubject] = useState('');
  const [bearerToken, setBearerToken] = useState('');
  const [loadingConfig, setLoadingConfig] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .getAuthConfig()
      .then((value) => {
        if (!cancelled) {
          setConfig(value);
        }
      })
      .catch((loadError: unknown) => {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : 'Unable to load authentication settings.');
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingConfig(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  if (auth.status === 'authenticated') {
    return <Navigate to="/runs" replace />;
  }

  const finishSignIn = async () => {
    const user = await apiClient.getCurrentUser();
    onAuthenticated(user);
    navigate('/runs', { replace: true });
  };

  const submitDevToken = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const response = await apiClient.issueDevToken({
        role: selectedRole,
        subject: subject.trim() || undefined,
      });
      apiClient.setAuthToken(response.token);
      await finishSignIn();
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Development token sign-in failed.');
    } finally {
      setSubmitting(false);
    }
  };

  const submitBearerToken = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      apiClient.setAuthToken(bearerToken.trim());
      await finishSignIn();
    } catch (submitError) {
      apiClient.clearAuthToken();
      setError(submitError instanceof Error ? submitError.message : 'Bearer token sign-in failed.');
    } finally {
      setSubmitting(false);
    }
  };

  const continueWithDevelopmentIdentity = async () => {
    setSubmitting(true);
    setError(null);
    try {
      apiClient.clearAuthToken();
      await finishSignIn();
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Development identity sign-in failed.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section className="panel narrow-panel auth-panel">
      <h1>Sign in required</h1>
      <p className="cell-meta">
        {loadingConfig ? 'Loading authentication settings…' : `Mode: ${config?.authentication ?? 'unavailable'}`}
      </p>

      {error ? <p className="validation-error" role="alert">{error}</p> : null}

      {config?.devIdentityEnabled ? (
        <button
          type="button"
          className="btn btn-primary btn-block"
          disabled={submitting}
          onClick={() => void continueWithDevelopmentIdentity()}
        >
          {submitting ? 'Signing in…' : 'Continue with development identity'}
        </button>
      ) : null}

      {config?.devTokensEnabled ? (
        <form className="auth-form" onSubmit={(event) => void submitDevToken(event)}>
          <label>
            <span>Development role</span>
            <select value={selectedRole} onChange={(event) => setSelectedRole(event.target.value as AuthRole)}>
              {AUTH_ROLES.map((role) => (
                <option key={role} value={role}>{role}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Subject</span>
            <input value={subject} onChange={(event) => setSubject(event.target.value)} placeholder="dev:viewer" />
          </label>
          <button type="submit" className="btn btn-secondary btn-block" disabled={submitting}>
            {submitting ? 'Signing in…' : 'Issue development token'}
          </button>
        </form>
      ) : null}

      <form className="auth-form" onSubmit={(event) => void submitBearerToken(event)}>
        <label>
          <span>Bearer token</span>
          <textarea
            rows={4}
            value={bearerToken}
            onChange={(event) => setBearerToken(event.target.value)}
            placeholder="Paste access token"
          />
        </label>
        <button type="submit" className="btn btn-secondary btn-block" disabled={submitting || !bearerToken.trim()}>
          {submitting ? 'Signing in…' : 'Use bearer token'}
        </button>
      </form>
    </section>
  );
}
