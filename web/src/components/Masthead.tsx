import type { SeasonPayload } from "../api/types"

export interface MastheadProps {
    payload: SeasonPayload | null;
    updatedAgo: string
    error: boolean
}

/*Masthead (header) for the site */
export default function Masthead({ payload, updatedAgo, error }: MastheadProps) {
    const text = payload ? `${payload.season} · Week ${payload.currentWeek} · Updated ${updatedAgo}` : error ? "Error loading..." : "";
    return (
        <header className="masthead">
            <div className="panel-inner">
                <h1>Bosty Base Fantasy Football</h1>
                <p className="muted">
                    {text}
                </p>
            </div>
        </header>
    )
}