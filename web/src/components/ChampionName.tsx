interface ChampionNameProps {
  name: string;
  /** What the crown is called: "Co-champion" when the title is shared. */
  title: string;
}

/** A champion's name with a crown and sparkles (T70). */
export default function ChampionName({ name, title }: ChampionNameProps) {
  // the crown keeps to the first word, so a wrapping name never strands it on a line of its own
  const space = name.indexOf(' ');
  const first = space < 0 ? name : name.slice(0, space);
  return (
    <span className="champion">
      <span className="champion-lead">
        <svg className="crown" role="img" aria-label={title} viewBox="0 0 16 16">
          <path d="M0.5 4 4.5 8 8 1.5l3.5 6.5 4-4L14 14.5H2z" />
        </svg>
        {first}
      </span>
      {name.slice(first.length)}
      <span className="sparkles" aria-hidden="true">
        <span className="sparkle" />
        <span className="sparkle" />
        <span className="sparkle" />
      </span>
    </span>
  );
}
