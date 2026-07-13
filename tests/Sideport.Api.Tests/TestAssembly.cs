using Xunit;

// The API suite includes intentionally short device deadline/recovery tests and
// process-backed system probes. Running those integration fixtures concurrently
// makes scheduler starvation look like a product timeout. Keep this assembly
// deterministic while the other solution test projects may still run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
