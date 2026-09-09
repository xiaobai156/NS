# Deterministic UI viewport

The high-DPI workspace bounds test now uses a child-form viewport instead of a top-level window constrained by the hosted desktop maximum tracking size. It additionally asserts the requested scaled height and retains every original positive-size and containment assertion. This is a test-environment change only: production form sizing, colors, controls, and layout are unchanged.
