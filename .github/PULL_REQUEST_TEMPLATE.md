This PR replaces stale references to 'Function1' with the real function name DataImportQueueTriggerFunction and removes the completed TODO about renaming the function.

Changes included:

- docs/IngestionPlan.md: replaced mentions of `Function1` with `DataImportQueueTriggerFunction` and reworded related sections for clarity.
- TODO.md: removed the "Rename `Function1`" checklist item and updated phrasing to reflect the rename.

Rationale:
- The codebase already contains the function class/file `DataImportQueueTriggerFunction`; the docs should reflect that to avoid confusion.

Signed-off-by: André
