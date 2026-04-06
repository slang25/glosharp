using System.Text;
var sb = new StringBuilder();
// ---cut-before---
sb.Append("hello");
//   ^?
sb.Append(" world");
var result = sb.ToString();
//    ^?
