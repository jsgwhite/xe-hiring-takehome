import { expect, test } from 'vitest'

// TEMPORARY - DELETE THIS FILE.
//
// A deliberately failing test, added to prove the CI workflow fails the build on a failing test
// rather than silently reporting green. Its backend counterpart is
// tests/RateAlerts.Api.Tests/CiPipelineCheck.cs.
//
// There is one per suite on purpose: the two run as independent CI jobs, so failing only the backend
// would leave the frontend job's failure reporting unverified.
//
// Delete both files once the red run has been observed.

test('deliberately fails to prove CI reports test failures', () => {
  expect(1).toBe(2)
})
