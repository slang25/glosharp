using System.Text;
var sb = new StringBuilder();
// @above-hidden
sb.Append("hello");
//   ^?
sb.Append(" world");
var result = sb.ToString();
//    ^?
