var users = LoadUsers();
// @log: Returns the cached list after the first call
var active = users.Where(u => u.IsActive);
// @warn: Count() walks the whole sequence
var count = active.Count();
// @annotate: Use .Any() if you only need a yes/no answer
// ---cut-after---
static User[] LoadUsers() => [new("ada", true), new("bob", false)];
record User(string Name, bool IsActive);
