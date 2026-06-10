import { PageHeader } from '../components/PageHeader';

interface PlaceholderProps {
  title: string;
  description: string;
}

export function Placeholder({ title, description }: PlaceholderProps) {
  return (
    <section>
      <PageHeader title={title} description={description} />
      <article className="panel">
        <p>This section is planned for a later implementation phase.</p>
      </article>
    </section>
  );
}
