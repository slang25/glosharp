using System.Text;
var sb = new StringBuilder();
// ---cut---
sb.Append("hello");
//   ^?
sb.Append(" world");
var result = sb.ToString();
//    ^?
