using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace UltimateCustomRun;

public sealed class Headstart : ModifierModel
{
    internal const string DisplayTitle = "Headstart";
    internal const string DisplayDescription = "Choose [blue]1[/blue] relic to start with.";
    protected override string IconPath => ImageHelper.GetImagePath("packed/modifiers/vintage.png");
    public override Func<Task>? GenerateNeowOption(EventModel eventModel) =>
        eventModel.Owner is { } player ? () => ChooseRelics(player) : null;

    internal static string DescriptionText(int count) =>
        $"Choose [blue]{count}[/blue] {(count == 1 ? "relic" : "relics")} to start with.";

    internal static List<RelicModel> Candidates(Player player)
    {
        var ownPool = player.Character.RelicPool;
        var otherCharacterIds = ModelDb.AllCharacterRelicPools.Where(pool => pool.Id != ownPool.Id)
            .SelectMany(pool => pool.AllRelicIds).Except(ownPool.AllRelicIds).ToHashSet();
        return FilterCandidates(player.UnlockState.Relics, otherCharacterIds, player.Relics,
            relic => relic.IsAllowedAtNeow(player));
    }

    internal static List<RelicModel> FilterCandidates(IEnumerable<RelicModel> unlocked,
        IReadOnlySet<ModelId> otherCharacterIds, IEnumerable<RelicModel> owned, Func<RelicModel, bool> allowed)
    {
        var ownedIds = owned.Select(relic => relic.Id).ToHashSet();
        return unlocked.Where(relic => relic is not Circlet and not DeprecatedRelic)
            .Where(relic => !otherCharacterIds.Contains(relic.Id))
            .Where(relic => relic.IsStackable || !ownedIds.Contains(relic.Id))
            .Where(allowed).DistinctBy(relic => relic.Id)
            // Use stable model IDs, not localized titles, for synchronized choice indexes.
            .OrderBy(relic => relic.Rarity).ThenBy(relic => relic.Id.ToString(), StringComparer.Ordinal).ToList();
    }

    internal static void ValidateSelection(IReadOnlyCollection<int> indexes, int count, int candidateCount)
    {
        if (indexes.Count != count || indexes.Distinct().Count() != count ||
            indexes.Any(index => index < 0 || index >= candidateCount))
            throw new InvalidOperationException("Headstart received an invalid relic selection.");
    }

    private static async Task ChooseRelics(Player player)
    {
        var candidates = Candidates(player);
        var count = ModifierValues.ForPlayer<Headstart>(player);
        if (candidates.Count < count) throw new InvalidOperationException("Headstart has too few eligible relics.");
        var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
        var context = new BlockingPlayerChoiceContext();
        var choiceId = synchronizer.ReserveChoiceId(player);
        await context.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
        List<int> indexes;
        try
        {
            if (LocalContext.IsMe(player))
            {
                var chosen = await HeadstartRelicUi.Select(candidates, count);
                indexes = chosen.Select(relic => candidates.IndexOf(relic)).ToList();
                ValidateSelection(indexes, count, candidates.Count);
                synchronizer.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromIndexes(indexes));
            }
            else indexes = (await synchronizer.WaitForRemoteChoice(player, choiceId)).AsIndexes();
            ValidateSelection(indexes, count, candidates.Count);
        }
        finally { await context.SignalPlayerChoiceEnded(); }
        // Use native acquisition, including pickup effects and removal from relic grab bags.
        // Close the picker first so relic effects can safely open their own selection screens.
        foreach (var index in indexes)
        {
            var relic = candidates[index];
            // An earlier pickup effect may have already granted another chosen relic.
            if (!relic.IsStackable && player.Relics.Any(owned => owned.Id == relic.Id)) continue;
            await RelicCmd.Obtain(relic.ToMutable(), player);
        }
    }
}

internal sealed class HeadstartSelection(int count, int candidateCount)
{
    private readonly SortedSet<int> _indexes = [];
    internal IReadOnlyCollection<int> Indexes => _indexes;
    internal bool CanConfirm => _indexes.Count == count;
    internal bool Toggle(int index)
    {
        if (index < 0 || index >= candidateCount) return false;
        if (_indexes.Remove(index)) return true;
        return _indexes.Count < count && _indexes.Add(index);
    }
}
