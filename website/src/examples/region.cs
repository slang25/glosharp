public class User
{
    public required string First { get; init; }
    public required string Last { get; init; }

    #region FullName
    public string FullName
        => $"{First} {Last}";
    #endregion
}
