# ADR-008: AI Tool-Calling Architecture (planned)
The AI must never receive free-text dashboard dumps as its only grounding;
it must call typed tools (GetServiceHealth, GetSLOStatus, ...) that read
real ATLAS data, and must cite what it used or say "Insufficient evidence."
Not yet implemented.
