public class User {
  public string First { get; set; }
  public string Last { get; set; }
  #region FullName
  public string FullName
    => $"{First} {Last}";
  #endregion
}