using System.Runtime.CompilerServices;

// EditMode tests (Tests.EditMode asmdef) drive TargetWaveManager through the
// internal seam InjectTargetsForTesting plus the GetBoundsForTesting /
// SetBoundsForTesting helpers — see TargetWaveManager.cs for rationale (issue
// #14). Without this attribute the test asmdef cannot reach those internals.
[assembly: InternalsVisibleTo("Tests.EditMode")]
