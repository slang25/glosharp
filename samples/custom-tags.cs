// @noErrors
var users = new[] { "alice", "bob", "carol" };
// @log: Returns a cached result after the first call
var filtered = users.Where(u => u.Length > 3);
// @warn: This allocates — avoid in hot paths
var count = filtered.Count();
// @error: Throws if the sequence is empty
var first = filtered.First();
// @annotate: Consider using .Any() if you only need a boolean check
