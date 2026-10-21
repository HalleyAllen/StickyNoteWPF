using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public class StoreData
{
    public List<StickyNoteModel> Notes { get; set; } = new();
    public List<TaskListModel> TaskLists { get; set; } = new();
}
