import { Link } from 'react-router-dom';

export function Login() {
  return (
    <section className="panel narrow-panel">
      <h1>Sign in required</h1>
      <p>Authentication integration will be connected in a later phase.</p>
      <Link to="/runs" className="btn btn-primary">
        Continue with mock session
      </Link>
    </section>
  );
}
