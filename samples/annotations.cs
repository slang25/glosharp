// @noErrors
var before = Compute(1);
// @highlight
var highlighted = Compute(2);
var after = Compute(3);
// @diff: +
var added = Compute(4);
// @diff: -
var removed = Compute(5);

static int Compute(int x) => x * 2;
