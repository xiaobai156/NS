# Audit repair phase 2

Non-zero worker exits preserve structured errors. Per-image errors remain available separately; GPU errors stop the task. Cancellation terminates only the owned process tree. Local cache keys include image content, worker source and model-weight hashes; empty results are not persisted. ASCII model copies use content-versioned roots. Atomic file replacement preserves the previous successful file.
