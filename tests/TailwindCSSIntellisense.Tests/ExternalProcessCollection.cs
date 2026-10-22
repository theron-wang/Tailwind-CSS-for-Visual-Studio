namespace TailwindCSSIntellisense.Tests;

// Cold shell/Node startup can exceed CLI deadlines when process tests compete on CI.
// Disable parallelization for this collection while retaining it for isolated tests.
[CollectionDefinition("External processes", DisableParallelization = true)]
public sealed class ExternalProcessCollection { }
