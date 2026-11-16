interface SpokenProps {
  /** What the page shows, such as "PF" or "13-5". */
  shown: string;
  /** What a screen reader says instead, such as "Points for". */
  spoken: string;
}

/** Short on screen, in full to a screen reader. */
export default function Spoken({ shown, spoken }: SpokenProps) {
  return (
    <>
      <span aria-hidden="true">{shown}</span>
      <span className="visually-hidden">{spoken}</span>
    </>
  );
}
