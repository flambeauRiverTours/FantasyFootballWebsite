interface SegmentedRowProps<T extends string> {
  /** Also the id stem for the label the group points at. */
  name: string;
  label: string;
  options: readonly (readonly [T, string])[];
  value: T;
  onChange: (next: T) => void;
}

/** A labelled row of exclusive options. */
export default function SegmentedRow<T extends string>({
  name,
  label,
  options,
  value,
  onChange,
}: SegmentedRowProps<T>) {
  const labelId = `${name}-label`;

  return (
    <div className="control-row">
      <span className="control-label" id={labelId}>
        {label}
      </span>
      <div className="segmented" role="group" aria-labelledby={labelId}>
        {options.map(([option, text]) => (
          <button
            key={option}
            type="button"
            aria-pressed={value === option}
            onClick={() => onChange(option)}
          >
            {text}
          </button>
        ))}
      </div>
    </div>
  );
}
