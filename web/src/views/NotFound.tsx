import { Link } from 'react-router-dom';

export function NotFound() {
  return (
    <section className="panel">
      <h1>Page not found</h1>
      <p>The page you are looking for does not exist.</p>
      <Link className="btn btn-primary" to="/runs">
        Go to Runs
      </Link>
    </section>
  );
}
