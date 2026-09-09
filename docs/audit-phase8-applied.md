# Coverage refinement

GPU traits are limited to methods that actually compute CUDA fingerprints. Pure rule, validation, and template-selection bookkeeping tests remain in the hosted suite. Added a midnight-state regression, repeated active-form disposal regression, and real child-process cancellation test. The cancellation test starts only its own sleeping PowerShell process, not OCR or external services.
