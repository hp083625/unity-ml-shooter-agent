using System.Runtime.CompilerServices;

// Tests reach into RayVisionTexture's internal helpers (ComputePixel, PaintRow,
// the TagHues table) so we don't have to bind a real RayPerceptionSensor in
// EditMode. The test asmdef is "Tests.EditMode".
[assembly: InternalsVisibleTo("Tests.EditMode")]
