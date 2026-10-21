using StickyNoteWPF.Services;
using Xunit;

namespace StickyNoteWPF.Tests;

public class StoreDataCodecTests
{
    [Fact]
    public void RepairsNullTreesAndPreservesRecoverableText()
    {
        var data = StoreDataCodec.Parse("""
            {"Notes":[null,{"Title":null,"Text":"retained","FontSize":-1,"Width":0,"Opacity":3}],
             "TaskLists":[null,{"Items":null},{"Items":[null,{"Text":"child","SubItems":null}]}]}
            """);
        Assert.Single(data.Notes);
        Assert.Equal("retained", data.Notes[0].Text);
        Assert.Equal(string.Empty, data.Notes[0].Title);
        Assert.Equal(14, data.Notes[0].FontSize);
        Assert.Equal(220, data.Notes[0].Width);
        Assert.Equal(1, data.Notes[0].Opacity);
        Assert.Empty(data.TaskLists[0].Items);
        Assert.Equal("child", Assert.Single(data.TaskLists[1].Items).Text);
        foreach (var list in data.TaskLists)
            using (var observer = new TaskTreeObserver(list.Items, () => { })) { }
    }

    [Fact]
    public void DuplicateAndEmptyIdsAreRepairedWithoutDroppingRecords()
    {
        var data = StoreDataCodec.Parse("""
            {"Notes":[{"Id":"11111111-1111-1111-1111-111111111111","Text":"one"},
                      {"Id":"11111111-1111-1111-1111-111111111111","Text":"two"}],
             "TaskLists":[{"Id":"00000000-0000-0000-0000-000000000000","Items":[
                 {"Id":"11111111-1111-1111-1111-111111111111","Text":"three"}]}]}
            """);
        var ids = data.Notes.Select(note => note.Id).Concat(data.TaskLists.Select(list => list.Id))
            .Concat(data.TaskLists.SelectMany(list => list.Items).Select(item => item.Id)).ToArray();
        Assert.Equal(4, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(new[] { "one", "two" }, data.Notes.Select(note => note.Text));
        Assert.Equal("three", data.TaskLists[0].Items[0].Text);
    }

    [Fact]
    public void LegacyArraysAndNormalizedDataRoundTrip()
    {
        var data = StoreDataCodec.Parse("""[{"Text":"legacy","Height":-10}]""");
        var restored = StoreDataCodec.Parse(StoreDataCodec.Serialize(data));
        Assert.Equal("legacy", Assert.Single(restored.Notes).Text);
        Assert.Equal(220, restored.Notes[0].Height);
        Assert.Equal(data.Notes[0].Id, restored.Notes[0].Id);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"unrecognized\":[]}")]
    public void UnrecognizedDocumentsAreRejectedInsteadOfBecomingEmptyStores(string json)
        => Assert.Throws<System.Text.Json.JsonException>(() => StoreDataCodec.Parse(json));
}
