// @suppressErrors
var users = GetUsers();
// @log: Returns cached result after first call
var filtered = users.Where(u => u.IsActive);
// @warn: This allocates — avoid in hot paths
var count = filtered.Count();
// @annotate: Consider using .Any() if you only need a boolean check