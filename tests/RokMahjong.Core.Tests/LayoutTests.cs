using RokMahjong.Core;

namespace RokMahjong.Core.Tests;

public class LayoutTests
{
    [Fact]
    public void Every_layout_matches_a_deck_size()
    {
        foreach (var layout in Layouts.All)
            Assert.Contains(Layouts.Decks, deck => deck.TileCount == layout.TileCount);
    }

    [Fact]
    public void Every_level_references_known_layout_and_deck()
    {
        Assert.Equal(12, Layouts.Levels.Count);
        foreach (var level in Layouts.Levels)
        {
            var layout = Layouts.GetLayout(level.LayoutId);
            var deck = Layouts.GetDeck(level.DeckId);
            Assert.Equal(layout.TileCount, deck.TileCount);
        }
    }

    [Fact]
    public void Three_layout_families_exist()
    {
        Assert.Equal(new[] { "Lotus", "Pagoda", "Turtle" }, Layouts.All.Select(l => l.Family).Distinct().OrderBy(x => x));
    }

    [Theory]
    [InlineData("lotus36", 36)]
    [InlineData("lotus72", 72)]
    [InlineData("pagoda72", 72)]
    [InlineData("pagoda108", 108)]
    [InlineData("pagoda144", 144)]
    [InlineData("turtle144", 144)]
    public void Layout_has_expected_slot_count(string id, int count)
    {
        Assert.Equal(count, Layouts.GetLayout(id).TileCount);
    }

    [Fact]
    public void Same_layer_slots_do_not_overlap()
    {
        foreach (var layout in Layouts.All)
        {
            var byLayer = layout.Slots.GroupBy(slot => slot.Z);
            foreach (var layer in byLayer)
            {
                var slots = layer.ToList();
                for (var i = 0; i < slots.Count; i++)
                    for (var j = i + 1; j < slots.Count; j++)
                        Assert.False(slots[i].Overlaps(slots[j]),
                            $"{layout.Id} layer {layer.Key} slots {slots[i]} and {slots[j]} overlap.");
            }
        }
    }
}
