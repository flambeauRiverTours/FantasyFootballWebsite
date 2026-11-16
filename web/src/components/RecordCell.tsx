import { gameResults, recordSpoken, recordText } from '../api/calculations';
import type { TeamRecord } from '../api/types';
import Spoken from './Spoken';

interface RecordCellProps {
  record: TeamRecord;
  /** Whether any record has a tie, so every record shows three figures. */
  showTies: boolean;
}

/** A record with its median split (A43). */
export default function RecordCell({ record, showTies }: RecordCellProps) {
  const { median } = record;
  const total = recordSpoken(record, showTies);
  if (!median) {
    return <Spoken shown={recordText(record, showTies)} spoken={total} />;
  }
  // a median tie needs two equal decimal scores: its share never shows ties
  const games = gameResults(record);
  const spoken = `${total}: ${recordSpoken(games, showTies)} in games, ${recordSpoken(median, false)} against the median`;
  return (
    <>
      <Spoken shown={recordText(record, showTies)} spoken={spoken} />
      <span className="record-split" aria-hidden="true">
        {recordText(games, showTies)} · {recordText(median, false)}
      </span>
    </>
  );
}
