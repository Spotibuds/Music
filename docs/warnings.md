# Compiler warning inventory

The baseline Release build produced 43 warnings. This branch reduces that to 34 without disabling nullable analysis or suppressing warnings globally.

## Addressed here

- 7 unused exception variables (`CS0168`) were changed to exception filters without a variable where the exception was intentionally converted into an existing fallback response.
- 2 nullable Redis endpoint warnings (`CS8604`) were addressed by returning a 503 when the Redis endpoint is not configured.

## Remaining categories

- 30 nullable-flow warnings (`CS8600`/`CS8602`) remain in `AzureBlobService`, controllers, and MongoDB diagnostics. They need targeted guards or explicit result types, but broad null-forgiving operators could hide real runtime failures.
- The remaining warning count is recorded by the build rather than hidden with `NoWarn` or a global suppression.

## Follow-up

The next safe warning pass should introduce explicit dependency-result types for Blob Storage and MongoDB, then add tests for unavailable dependencies before changing those control flows.
