var config = LoadConfig();
var conn = config.GetConnection();
// @highlight
var db = new DbContext(conn);