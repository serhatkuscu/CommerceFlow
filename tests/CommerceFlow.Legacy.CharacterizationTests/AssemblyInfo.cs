// Tests in different xUnit collections still run in parallel with each other by default; this
// assembly is entirely DB-driven right now, so that default would let two collections hit the
// same container concurrently. Belt-and-suspenders alongside the single "Database" collection.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
