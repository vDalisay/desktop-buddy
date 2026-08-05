using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Marks the selected entry of a Win98 list with a check mark. ItemList has no per-item check
/// state, so the mark is a text prefix, re-applied on draw because rebuilds and code-driven
/// <see cref="ItemList.Select"/> calls emit no selection signal.
/// </summary>
public static class Win98ItemListCheck
{
    private const string Mark = "✓ ";
    private const string Blank = "  ";

    public static void Attach(ItemList list) => list.Draw += () => Apply(list);

    private static void Apply(ItemList list)
    {
        for (int index = 0; index < list.ItemCount; index++)
        {
            string text = list.GetItemText(index);
            string bare = text.StartsWith(Mark) || text.StartsWith(Blank)
                ? text[Mark.Length..]
                : text;
            string wanted = (list.IsSelected(index) ? Mark : Blank) + bare;
            if (text != wanted)
                list.SetItemText(index, wanted);
        }
    }
}
