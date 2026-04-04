var todos = new List<Todo>
{
    new("Learn Glo#", true),
    new("Build docs", false),
};

var pending = todos.Where(t => !t.Done);
//^?

// @highlight
Console.WriteLine($"Pending: {pending.Count()}");
// @hide
/// <summary>A task with a title and completion status.</summary>
public record Todo(string Title, bool Done);