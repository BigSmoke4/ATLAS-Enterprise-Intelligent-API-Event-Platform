# ADR-005: Why MVC/Razor instead of SPA
Requirement-driven: an operations control-room UI benefits from
server-rendered, high-density views and progressive enhancement (SignalR
for the few genuinely real-time panels) over full client-side app state;
also avoids a second API contract just to feed a SPA.
