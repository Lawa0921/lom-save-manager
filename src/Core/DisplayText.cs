namespace LoMSaveManager
{
    /// <summary>
    /// User-typed strings (save and category names) as UI text. The game's Text components render Unity rich text, so a
    /// name like "&lt;size=300&gt;" would restyle the panel; every '&lt;' is shown as the full-width '＜' instead, which
    /// can never open a tag. Only the displayed copy changes: the stored name and an edit field's prefill stay as typed.
    /// </summary>
    public static class DisplayText
    {
        public static string Plain(string text)
        {
            return text == null ? null : text.Replace('<', '\uFF1C');
        }
    }
}
