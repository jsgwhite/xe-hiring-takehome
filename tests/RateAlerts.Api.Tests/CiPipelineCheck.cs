namespace RateAlerts.Api.Tests;

/// <summary>
/// TEMPORARY - DELETE THIS FILE.
///
/// A deliberately failing test, added to prove the CI workflow actually fails the build on a failing
/// test rather than silently reporting green. The commit before this one is green, so the pair of runs
/// together show the pipeline discriminating between pass and fail.
///
/// Delete this file (and its frontend counterpart, src/ciPipelineCheck.test.ts) once the red run has
/// been observed.
/// </summary>
public class CiPipelineCheck
{
    [Fact]
    public void Deliberately_fails_to_prove_CI_reports_test_failures()
    {
        const int expected = 1;
        const int actual = 2;

        Assert.Equal(expected, actual);
    }
}
