// ---cut-start---
// What `glosharp render User.cs --region FullName` shows for region.cs: just
// the region, compiled in the context of the whole file. The website renders
// through Expressive Code, so cut markers reproduce that output here.
public class User
{
    public required string First { get; init; }
    public required string Last { get; init; }

// ---cut-end---
    public string FullName
        => $"{First} {Last}";
// ---cut-start---
}
