# Hosted desktop fixture

The failure was confirmed to be an actual test-window clamp at 1044x788, exactly the hosted desktop MaxWindowTrackSize. The production application layout is unchanged. The original top-level form test remains intact and additionally verifies its requested scaled height. A guarded fixture configures only GitHub-hosted CI desktops to at least 1920x1080 before GUI tests. The helper refuses to change local or self-hosted desktops. It does not install drivers, run OCR, access user images or deploy software.
