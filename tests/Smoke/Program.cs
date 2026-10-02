using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Cards;
using UltimateCustomRun;
using Friendship = UltimateCustomRun.Friendship;

internal static class Program
{
    private static void Main(string[] args)
    {
        var dataPath = Path.Combine(args.Length > 0 ? args[0]
            : @"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2", "data_sts2_windows_x86_64");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(dataPath, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var assembly = typeof(ModEntry).Assembly;
        Check(assembly.GetName().Name == "UltimateCustomRun" && typeof(ModEntry).Namespace == "UltimateCustomRun" &&
            ModEntry.HarmonyId == "vertopolka.UltimateCustomRun", "Assembly, namespace and Harmony identity are UltimateCustomRun");
        var initializer = typeof(ModEntry).GetCustomAttribute<ModInitializerAttribute>();
        Check(initializer?.initializerMethod == nameof(ModEntry.Initialize), "Game loader can find Initialize");

        // Real Harmony patching against the installed game, outside the Godot UI.
        var harmony = new Harmony(ModEntry.HarmonyId);
        try
        {
            ModEntry.ApplyPatches();
            Check(!ModelDb.Contains(typeof(NeowStarterChoice)),
                "Mod initializer leaves content registration to game startup");
            var target = typeof(Specialized).GetMethod(nameof(Specialized.GenerateNeowOption))!;
            var patched = Harmony.GetPatchInfo(target);
            Check(patched == null || !patched.Owners.Contains(ModEntry.HarmonyId),
                "Original Specialized is not patched");
            Check(NativeModifierCountPatch.Targets.All(target => Harmony.GetPatchInfo(
                AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)))?.Owners.Contains(ModEntry.HarmonyId) == true),
                "All six native modifier count state machines are patched");

            // Avoid starting a run or constructing native Godot objects.
            var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
            var specialized = (Specialized)RuntimeHelpers.GetUninitializedObject(typeof(Specialized));
            var vanilla = specialized.GenerateNeowOption(neow);
            Check(vanilla.Method.DeclaringType!.Assembly == typeof(Specialized).Assembly,
                "Uninitialized event retains vanilla callback");

            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow, player);
            var unchanged = specialized.GenerateNeowOption(neow);
            Check(unchanged.Method.DeclaringType!.Assembly == typeof(Specialized).Assembly,
                "Original Specialized retains vanilla random callback");
            var pickAny = (SpecializedPickAny)RuntimeHelpers.GetUninitializedObject(typeof(SpecializedPickAny));
            var draft = (SpecializedDraft)RuntimeHelpers.GetUninitializedObject(typeof(SpecializedDraft));
            Check(pickAny.GenerateNeowOption(neow)?.Method.DeclaringType?.Assembly == assembly &&
                draft.GenerateNeowOption(neow)?.Method.DeclaringType?.Assembly == assembly,
                "Both new modifiers provide their own Neow buttons");

            TestNeowModifier();
            TestGroups();
            TestSpecializedVariants();
            TestModifierValues();
            TestRadioChains();
            TestColorlessCards();
            TestAscensionModifiers();
            TestDailyIsolation();
            TestSuperModifiers();
            TestSealedSliders();
            TestSpeedrun();
            TestPresetManagement();
            TestUltimateStarter();
            TestDill();
        }
        finally
        {
            harmony.UnpatchAll(ModEntry.HarmonyId);
        }
        Console.WriteLine("PASS: managed integration smoke checks. UI/deck acquisition require an in-game playtest.");
    }

    private static void TestGroups()
    {
        Check(ModifierGroups.Sections.Select(s => s.Title).SequenceEqual(
            new[] { "Improved Start", "Run Parameters", "Modifiers", "Card Pool", "Ascension", "Negatives", "Disabled" }), "Group titles and order match the requested layout");
        var negatives = new HashSet<Type> { typeof(BigGameHunter), typeof(CursedRun), typeof(DeadlyEvents),
            typeof(Midas), typeof(Murderous), typeof(NightTerrors), typeof(Terminal) };
        foreach (var type in new[] { typeof(NeowStarterChoice), typeof(Specialized), typeof(SpecializedPickAny), typeof(SpecializedDraft), typeof(Draft),
            typeof(SealedDeck), typeof(Insanity), typeof(AllStar), typeof(AllStarDraft) })
            Check(ModifierGroups.Classify((ModifierModel)RuntimeHelpers.GetUninitializedObject(type), negatives) == ModifierGroup.ImprovedStart,
                type.Name + " belongs to Improved Start");
        Check(ModifierGroups.Classify(ModelDb.Modifier<CharacterCards>(), negatives) == ModifierGroup.CardPool,
            "Character card pools belong to Card Pool");
        foreach (var type in negatives)
            Check(ModifierGroups.Classify((ModifierModel)RuntimeHelpers.GetUninitializedObject(type), negatives) == ModifierGroup.Negatives,
                type.Name + " belongs to Negatives");
        Check(ModifierGroups.Classify(ModelDb.Modifier<Hoarder>(), negatives) == ModifierGroup.Modifiers &&
            ModifierGroups.Classify(ModelDb.Modifier<Vintage>(), negatives) == ModifierGroup.Modifiers &&
            ModifierGroups.Classify(ModelDb.Modifier<Flight>(), negatives) == ModifierGroup.Modifiers,
            "Other modifiers stay in Modifiers");
    }

    private static void TestNeowModifier()
    {
        ModelDb.Init([typeof(Draft), typeof(SealedDeck), typeof(Hoarder), typeof(Specialized),
            typeof(Insanity), typeof(AllStar), typeof(Flight), typeof(Vintage), typeof(CharacterCards), typeof(NeowStarterChoice),
            typeof(SpecializedPickAny), typeof(SpecializedDraft), typeof(AllStarDraft), typeof(ColorlessCards),
            typeof(Friendship), typeof(FriendshipDraft), typeof(RichLoot), typeof(CardSwarm), typeof(CustomRunParameters),
            typeof(SuperDraft), typeof(MustHave), typeof(Speedrun), typeof(UltimateStarter), typeof(Dill),
            typeof(UltimateStrike), typeof(UltimateDefend),
            typeof(StrikeIronclad), typeof(DefendIronclad), typeof(Bash),
            typeof(StrikeSilent), typeof(DefendSilent), typeof(Neutralize), typeof(Survivor),
            typeof(StrikeRegent), typeof(DefendRegent), typeof(FallingStar), typeof(Venerate),
            typeof(StrikeNecrobinder), typeof(DefendNecrobinder), typeof(Bodyguard), typeof(Unleash),
            typeof(StrikeDefect), typeof(DefendDefect), typeof(Zap), typeof(Dualcast),
            typeof(MegaCrit.Sts2.Core.Models.Relics.DingyRug),
            typeof(BigGameHunter), typeof(CursedRun), typeof(DeadlyEvents), typeof(Midas), typeof(Murderous), typeof(NightTerrors), typeof(Terminal),
            typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad), typeof(MegaCrit.Sts2.Core.Models.Characters.Silent),
            typeof(MegaCrit.Sts2.Core.Models.Characters.Regent), typeof(MegaCrit.Sts2.Core.Models.Characters.Necrobinder), typeof(MegaCrit.Sts2.Core.Models.Characters.Defect),
            typeof(MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool), typeof(Neow),
            typeof(SwarmingElites), typeof(WearyTraveler), typeof(Poverty), typeof(TightBelt), typeof(UltimateCustomRun.AscendersBane),
            typeof(Inflation), typeof(Scarcity), typeof(ToughEnemies), typeof(DeadlyEnemies), typeof(DoubleBoss)]);
        Check(ModelDb.All.Count(model => model is NeowStarterChoice) == 1,
            "Game startup constructs Neow!! once without DuplicateModelException");
        // Fixture for model IDs without invoking the engine-dependent startup logger.
        var cacheType = typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache);
        var ids = ModelDb.All.Select(m => ModelDb.GetId(m.GetType())).ToArray();
        var categories = (Dictionary<string, int>)AccessTools.Field(cacheType, "_categoryNameToNetIdMap").GetValue(null)!;
        foreach (var id in ids.Select(id => id.Category).Distinct()) categories[id] = categories.Count;
        var entries = (Dictionary<string, int>)AccessTools.Field(cacheType, "_entryNameToNetIdMap").GetValue(null)!;
        foreach (var id in ids.Select(id => id.Entry).Distinct()) entries[id] = entries.Count;
        entries[ModelDb.GetId(typeof(TestNeow)).Entry] = entries.Count;
        var categoryNames = (List<string>)AccessTools.Field(cacheType, "_netIdToCategoryNameMap").GetValue(null)!;
        categoryNames.Clear();
        categoryNames.AddRange(categories.OrderBy(pair => pair.Value).Select(pair => pair.Key));
        var entryNames = (List<string>)AccessTools.Field(cacheType, "_netIdToEntryNameMap").GetValue(null)!;
        entryNames.Clear();
        entryNames.AddRange(entries.OrderBy(pair => pair.Value).Select(pair => pair.Key));
        AccessTools.Property(cacheType, "CategoryIdBitSize").SetValue(null, (int)Math.Ceiling(Math.Log2(categoryNames.Count)));
        AccessTools.Property(cacheType, "EntryIdBitSize").SetValue(null, (int)Math.Ceiling(Math.Log2(entryNames.Count)));
        AccessTools.Field(cacheType, "_initialized").SetValue(null, true);
        foreach (var type in ModelDb.All.Select(model => model.GetType()).Distinct())
            AccessTools.Method(cacheType, "CachePropertiesForType").Invoke(null, [type, null, null]);
        AccessTools.Property(cacheType, "PropertyIdBitSize").SetValue(null,
            (int)Math.Ceiling(Math.Log2(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.MaxPropertyId + 1)));
        ModelDb.InitIds();
        Check(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers).Count(m => m is NeowStarterChoice) == 1,
            "Neow!! is registered once in the modifier list");
        var modifier = ModelDb.Modifier<NeowStarterChoice>();
        Check(ModifierModel.FromSerializable(modifier.ToMutable().ToSerializable()) is NeowStarterChoice,
            "New modifier round-trips through game save serialization");
        Check(NeowStarterChoice.DisplayTitle == "Neow!!" &&
            NeowStarterChoice.DisplayDescription == "Brings back starter Neow relic choice",
            "Requested title and description are exact");

        var test = new TestNeow();
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        IReadOnlyList<ModifierModel> modifiers = [modifier, ModelDb.Modifier<Specialized>()];
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, modifiers);
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(test, player);
        AccessTools.Method(typeof(AbstractModel), "InitId").Invoke(test, [ModelDb.GetId<Neow>()]);
        Check(NeowRelicChoice.NeedsRelicChoices(test), "Marker enables starter relic choices");
        Check(NeowRelicChoice.ModifierCount(2, test) == 2, "Initial modifier chain remains enabled");
        AccessTools.Method(typeof(EventModel), "SetEventFinished").Invoke(test,
            [new LocString("events", test.Id.Entry + ".pages.DONE.description")]);
        Check(test.OptionsShown?.Count == 3 && !test.IsFinished,
            "Modifier-chain completion shows three relic offers instead of ending event");
        Check(!NeowRelicChoice.NeedsRelicChoices(test), "Relic choice is restored only once");
        Check(ReferenceEquals(run.Modifiers, modifiers) && run.Modifiers.Count == 2,
            "Restoring relics does not remove or replace run modifiers");
        Check(NeowRelicChoice.ModifierCount(2, test) == 2, "Temporary vanilla-generation scope is cleared");

        var failing = new TestNeow();
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(failing, player);
        failing.FailGeneration = true;
        try { NeowRelicChoice.GenerateRelicChoices(failing); throw new Exception("Expected generation failure"); }
        catch (TargetInvocationException) { }
        Check(NeowRelicChoice.NeedsRelicChoices(failing) && NeowRelicChoice.ModifierCount(2, failing) == 2,
            "Failed generation resets restoration state and scope");
    }

    private static void TestSpecializedVariants()
    {
        var pickAny = ModelDb.Modifier<SpecializedPickAny>();
        var draft = ModelDb.Modifier<SpecializedDraft>();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        Check(custom.Count(m => m is SpecializedPickAny) == 1 &&
            custom.Count(m => m is SpecializedDraft) == 1,
            "Specialized variants appear once in the same mod's modifier list");
        Check(ModifierModel.FromSerializable(pickAny.ToMutable().ToSerializable()) is SpecializedPickAny &&
            ModifierModel.FromSerializable(draft.ToMutable().ToSerializable()) is SpecializedDraft,
            "Both Specialized variants round-trip through game save serialization");
        Check(SpecializedExclusivityPatch.ShouldUntick(pickAny, draft) &&
            SpecializedExclusivityPatch.ShouldUntick(pickAny, ModelDb.Modifier<Specialized>()) &&
            SpecializedExclusivityPatch.ShouldUntick(draft, ModelDb.Modifier<Specialized>()),
            "Three Specialized modes are mutually exclusive in custom runs");
        var allStarDraft = ModelDb.Modifier<AllStarDraft>();
        Check(custom.Count(m => m is AllStarDraft) == 1 &&
            ModifierModel.FromSerializable(allStarDraft.ToMutable().ToSerializable()) is AllStarDraft,
            "All Star draft is registered once and survives save serialization");
        var order = custom.Select(m => m.GetType()).ToList();
        Check(order[order.IndexOf(typeof(AllStar)) + 1] == typeof(AllStarDraft),
            "All Star draft appears next to original All Star");
        Check(order[order.IndexOf(typeof(Specialized)) + 1] == typeof(SpecializedDraft) &&
            order[order.IndexOf(typeof(Specialized)) + 2] == typeof(SpecializedPickAny),
            "Specialized ordering remains normal, draft, any");
        var colorlessPool = ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>();
        var colorlessOptions = AllStarDraft.Options(colorlessPool);
        Check(AllStarDraft.RewardCount == 5 && colorlessOptions.CardPools.Single() == colorlessPool &&
            colorlessOptions.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications),
            "Five All Star draft rewards stay in the Colorless pool");
        Check(SpecializedExclusivityPatch.ShouldUntick(allStarDraft, ModelDb.Modifier<AllStar>()),
            "All Star and All Star draft are mutually exclusive in custom runs");
        Check(SpecializedDraft.DisplayDescription == "Choose [blue]1[/blue] card reward. Add [blue]5[/blue] copies yo your starting deck.",
            "Specialized draft uses requested wording and blue numbers");
        Check(SpecializedDraft.DisplayTitle == "Specialized - Draft" &&
            SpecializedPickAny.DisplayTitle == "Specialized - Pick Any" && AllStarDraft.DisplayTitle == "All Star - Draft",
            "Only variant titles use the requested hyphen");
        const string rowTemplate = "[{color}]{modifier_title}[/{color}]: {modifier_description}";
        var uiTable = new LocTable("main_menu_ui", new Dictionary<string, string> { ["CUSTOM_RUN_SCREEN.MODIFIER_LABEL"] = rowTemplate });
        AccessTools.Method(typeof(ModifierTextPatch), "Postfix").Invoke(null, ["main_menu_ui", uiTable]);
        Check(uiTable.GetRawText("CUSTOM_RUN_SCREEN.MODIFIER_LABEL") == rowTemplate,
            "Native colon between modifier title and description remains unchanged");
        var readyPatches = Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NCustomRunModifiersList), "_Ready"));
        Check(readyPatches?.Transpilers.Any(p => p.owner == ModEntry.HarmonyId) == true,
            "Custom modifiers are injected at the actual UI list creation call site");
        Check(SpecializedDraftReward.OfferCount == 3 && SpecializedCardChoices.Copies == 5,
            "Draft offers three cards and acquisition grants five copies");
        var cardType = typeof(CardModel).Assembly.GetTypes().First(type => !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type));
        var first = (CardModel)RuntimeHelpers.GetUninitializedObject(cardType);
        var second = (CardModel)RuntimeHelpers.GetUninitializedObject(cardType);
        var poolType = typeof(CardPoolModel).Assembly.GetTypes().First(type => !type.IsAbstract && typeof(CardPoolModel).IsAssignableFrom(type));
        var pool = (CardPoolModel)RuntimeHelpers.GetUninitializedObject(poolType);
        Check(SpecializedDraftReward.Options(pool).RarityOdds == CardRarityOddsType.Uniform,
            "Draft uses all-rarity uniform reward generation");
        var offers = new MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult[] { new(first), new(second) };
        Check(ReferenceEquals(SpecializedDraftReward.ResolveSelection(offers, 1), second),
            "Draft grants the selected reward rather than the first offer");
        Check(SpecializedDraftReward.ResolveSelection(offers, null) == null, "Skipped rewards do not grant cards");
        foreach (var index in new[] { -1, 2 })
        {
            try { SpecializedDraftReward.ResolveSelection(offers, index); throw new Exception("Expected invalid selection rejection"); }
            catch (InvalidOperationException) { }
        }
        Check(true, "Invalid reward indices are rejected");
    }

    private static void TestModifierValues()
    {
        var cases = new (ModifierModel Model, int Min, int Max, int Step, int Default)[]
        {
            (ModelDb.Modifier<Specialized>(), 1, 10, 1, 5),
            (ModelDb.Modifier<SpecializedDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<SpecializedPickAny>(), 1, 10, 1, 5),
            (ModelDb.Modifier<AllStar>(), 1, 10, 1, 5),
            (ModelDb.Modifier<AllStarDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<Friendship>(), 1, 10, 1, 5),
            (ModelDb.Modifier<FriendshipDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<Draft>(), 5, 20, 5, 10),
            (ModelDb.Modifier<SealedDeck>(), 5, 25, 5, 10),
            (ModelDb.Modifier<Insanity>(), 5, 60, 5, 30),
            (ModelDb.Modifier<Hoarder>(), 1, 5, 1, 2),
            (ModelDb.Modifier<Midas>(), 150, 300, 5, 200)
        };
        foreach (var (canonical, min, max, step, defaultValue) in cases)
        {
            var spec = ModifierValues.For(canonical)!;
            Check((spec.Min, spec.Max, spec.Step, spec.Default) == (min, max, step, defaultValue),
                canonical.GetType().Name + " has the requested range, step and vanilla default");
            var model = canonical.ToMutable();
            Check(ModifierValues.Get(model) == defaultValue, "Unconfigured modifier preserves default: " + model.GetType().Name);
            for (var value = min; value <= max; value += step)
            {
                ModifierValues.Set(model, value);
                var serializable = model.ToSerializable();
                var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
                serializable.Props!.Serialize(writer);
                var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
                reader.Reset(writer.Buffer);
                var props = reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SavedProperties>();
                var fromNetwork = ModifierModel.FromSerializable(new MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier
                    { Id = serializable.Id, Props = props });
                Check(ModifierValues.Get(ModifierModel.FromSerializable(serializable)) == value &&
                    ModifierValues.Get(fromNetwork) == value &&
                    ModifierValues.Get((ModifierModel)model.MutableClone()) == value,
                    $"{model.GetType().Name} value {value} survives save, network properties and clone");
            }
            ModifierValues.Set(model, int.MinValue);
            Check(ModifierValues.Get(model) == min, "Lower bound enforced: " + model.GetType().Name);
            ModifierValues.Set(model, int.MaxValue);
            Check(ModifierValues.Get(model) == max, "Upper bound enforced: " + model.GetType().Name);
            if (step > 1)
            {
                ModifierValues.Set(model, min + 3);
                Check(ModifierValues.Get(model) == min + step, "Off-step values normalized: " + model.GetType().Name);
            }
            Check(ModifierValues.Get(canonical) == defaultValue, "Configuration cannot leak to canonical/daily model: " + model.GetType().Name);
        }
        var midas = ModelDb.Modifier<Midas>().ToMutable();
        ModifierValues.Set(midas, 155);
        Check(ModifierValues.ScaleGold(20, midas) == 31 && ModifierValues.ScaleGold(21, midas) == 32,
            "Midas uses the selected percent with integer gold rounding");
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        var insanity = ModelDb.Modifier<Insanity>().ToMutable();
        ModifierValues.Set(insanity, 60);
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { insanity });
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        Check(ModifierValues.ForPlayer<Insanity>(player) == 60, "Native gameplay resolves the run's selected count");
        var rewards = new List<MegaCrit.Sts2.Core.Rewards.Reward>
            { new MegaCrit.Sts2.Core.Rewards.GoldReward(20, player), new MegaCrit.Sts2.Core.Rewards.GoldReward(21, player) };
        ((Midas)midas).TryModifyRewardsLate(player, rewards, null);
        Check(rewards.Cast<MegaCrit.Sts2.Core.Rewards.GoldReward>().Select(reward => reward.Amount).SequenceEqual(new[] { 31, 32 }),
            "Patched native Midas reward method applies 155 percent instead of its fixed multiplier");
        foreach (var target in NativeModifierCountPatch.Targets)
        {
            var instructions = PatchProcessor.GetCurrentInstructions(AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)));
            Check(instructions.Count(instruction => instruction.opcode == System.Reflection.Emit.OpCodes.Call &&
                instruction.operand is MethodInfo method && method.DeclaringType == typeof(ModifierValues) &&
                method.Name is nameof(ModifierValues.ForPlayer) or nameof(ModifierValues.Get)) == 1,
                target.Type.Name + " gameplay replaces exactly one count with the configured resolver");
        }
    }

    private static void TestColorlessCards()
    {
        var modifier = ModelDb.Modifier<ColorlessCards>();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        Check(custom.Count(m => m is ColorlessCards) == 1 &&
            ModifierGroups.Classify(modifier, new HashSet<Type>()) == ModifierGroup.CardPool,
            "Colorless Cards appears once in Card Pool");
        Check(custom[0] is ColorlessCards, "Colorless relic is granted before starting card rewards");
        Check(ModifierModel.FromSerializable(modifier.ToMutable().ToSerializable()) is ColorlessCards,
            "Colorless Cards survives save serialization");
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var relics = (List<RelicModel>)AccessTools.Field(typeof(Player), "_relics").GetValue(player)!;
        // Create the native collection for this engine-free fixture.
        if (relics == null)
        {
            relics = [];
            AccessTools.Field(typeof(Player), "_relics").SetValue(player, relics);
        }
        relics.Add(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DingyRug>());
        var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow, player);
        modifier.GenerateNeowOption(neow)!().GetAwaiter().GetResult();
        Check(relics.Count == 1, "Existing Dingy Rug does not get granted twice");
        Check(typeof(ColorlessCards).GetMethod(nameof(ModifierModel.ModifyCardRewardCreationOptions))!.DeclaringType != typeof(ColorlessCards),
            "Colorless card rewards are handled by the native relic, not a duplicate modifier hook");
        var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(RelicModel), nameof(RelicModel.IsAllowed)));
        Check(patches == null || !patches.Owners.Contains(ModEntry.HarmonyId), "Relic availability is not patched");
        Check(!ColorlessCards.DisplayDescription.Contains("Dingy", StringComparison.OrdinalIgnoreCase),
            "Public description only describes colorless rewards");
    }

    private static void TestRadioChains()
    {
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        var start = custom.Where(modifier => ModifierGroups.Classify(modifier, new HashSet<Type>()) == ModifierGroup.ImprovedStart).ToArray();
        Check(start.Take(3).Select(modifier => modifier.GetType()).SequenceEqual(new[] { typeof(Draft), typeof(SealedDeck), typeof(Insanity) }),
            "Draft, Sealed Deck and Insanity are adjacent for their shared chain");
        var linked = Enumerable.Range(1, start.Length - 1).Where(index => LinkedModifierChains.AreLinked(start[index - 1], start[index])).ToArray();
        Check(linked.Length == 6, "Six adjacent mutually exclusive pairs receive chains");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<Draft>(), ModelDb.Modifier<SealedDeck>()) &&
            LinkedModifierChains.AreLinked(ModelDb.Modifier<SealedDeck>(), ModelDb.Modifier<Insanity>()), "Native deck replacement choices are linked");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<Specialized>(), ModelDb.Modifier<SpecializedDraft>()) &&
            LinkedModifierChains.AreLinked(ModelDb.Modifier<SpecializedDraft>(), ModelDb.Modifier<SpecializedPickAny>()), "Specialized choices are linked");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<AllStar>(), ModelDb.Modifier<AllStarDraft>()), "All Star choices are linked");
        Check(!LinkedModifierChains.AreLinked(ModelDb.Modifier<Insanity>(), ModelDb.Modifier<Specialized>()) &&
            !LinkedModifierChains.AreLinked(ModelDb.Modifier<AllStarDraft>(), ModelDb.Modifier<NeowStarterChoice>()), "Independent modifiers are not visually linked");
    }

    private static void TestAscensionModifiers()
    {
        var levels = Enum.GetValues<MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel>().Where(level => (int)level > 0).ToArray();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        var effects = custom.OfType<AscensionModifier>().ToArray();
        Check(effects.Select(effect => effect.Level).SequenceEqual(levels), "All ten ascension effects appear once in level order");
        Check(ModifierListPatch.ForCustomRun(custom).OfType<AscensionModifier>().Count() == levels.Length,
            "Repeated custom enumeration does not duplicate ascension modifiers");
        foreach (var effect in effects)
        {
            Check(ModifierGroups.Classify(effect, new HashSet<Type>()) == ModifierGroup.Ascension && ModifierListPatch.IsCustomOnly(effect),
                effect.GetType().Name + " is custom-only and belongs to Ascension");
            var saved = effect.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            var network = reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>();
            var restored = ModifierModel.FromSerializable(network);
            Check(restored is AscensionModifier ascension && ascension.Level == effect.Level &&
                ModifierModel.FromSerializable(saved).GetType() == effect.GetType(),
                effect.GetType().Name + " survives save and network serialization");
            var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { restored });
            var manager = new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(0);
            IndependentAscensionPatch.Bind(manager, run);
            foreach (var level in levels)
                Check(manager.HasLevel(level) == (level == effect.Level), effect.GetType().Name + " activates only " + level);
        }
        var combined = new ModifierModel[] { ModelDb.Modifier<DoubleBoss>().ToMutable(), ModelDb.Modifier<TightBelt>().ToMutable(), ModelDb.Modifier<RichLoot>().ToMutable() };
        Check(AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.DoubleBoss) &&
            AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.TightBelt) &&
            !AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.Poverty), "Selected ascension effects combine without enabling intervening levels");
        Check(AscensionModifiers.WithoutAscensions(combined).Single() == combined[2], "Changing ascension removes all independent effects and preserves other modifiers");
        var vanilla = new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(10);
        Check(levels.All(vanilla.HasLevel), "Unbound vanilla Ascension 10 still enables every level");
        foreach (var target in new[] { "OnModifiersListChanged", "AscensionChanged" })
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen), target))?.Owners.Contains(ModEntry.HarmonyId) == true,
                "Ascension/modifier selection synchronization is patched: " + target);
    }

    private static void TestDailyIsolation()
    {
        foreach (var property in new[] { nameof(ModelDb.GoodModifiers), nameof(ModelDb.BadModifiers), nameof(ModelDb.MutuallyExclusiveModifiers) })
        {
            var info = Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(ModelDb), property));
            Check(info == null || !info.Owners.Contains(ModEntry.HarmonyId), "Daily global pool is not patched: " + property);
        }
        Check(!ModelDb.GoodModifiers.Any(ModifierListPatch.IsCustomOnly), "Daily cannot roll any of our custom-only modifiers");
        Check(ModelDb.GoodModifiers.Select(m => m.GetType()).SequenceEqual(new[]
            { typeof(Draft), typeof(SealedDeck), typeof(Hoarder), typeof(Specialized), typeof(Insanity), typeof(AllStar), typeof(Flight), typeof(Vintage), typeof(CharacterCards) }),
            "Vanilla good-modifier count and order are preserved");
        string Signature(ulong seed) => string.Join("|", ModifierModel.Pick2Good1Bad(new MegaCrit.Sts2.Core.Random.Rng(seed), [])
            .Select(m => m is CharacterCards cards ? m.Id + "/" + cards.CharacterModel : m.Id.ToString()));
        var withMod = Enumerable.Range(0, 100).Select(seed => Signature((ulong)seed)).ToArray();
        var harmony = new Harmony(ModEntry.HarmonyId);
        harmony.UnpatchAll(ModEntry.HarmonyId);
        try
        {
            var vanilla = Enumerable.Range(0, 100).Select(seed => Signature((ulong)seed)).ToArray();
            Check(withMod.SequenceEqual(vanilla), "100 daily seeds select exactly the same modifiers with and without our patches");
        }
        finally { ModEntry.ApplyPatches(); }
    }

    private static void TestSuperModifiers()
    {
        var custom = ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers));
        foreach (var type in new[] { typeof(SuperDraft), typeof(MustHave) })
        {
            var model = custom.Single(modifier => modifier.GetType() == type);
            Check(ModifierListPatch.IsCustomOnly(model) && ModifierValues.For(model) == null,
                type.Name + " is custom-only, appears once and has no adjustable values");
            Check(ModifierModel.FromSerializable(model.ToSerializable()).GetType() == type,
                type.Name + " survives save serialization");
            var saved = model.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            Check(ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()).GetType() == type,
                type.Name + " survives multiplayer serialization");
        }
        Check(!ModelDb.Modifier<SuperDraft>().ClearsPlayerDeck, "Super Draft adds to the starting deck");
        for (var index = 0; index <= 12; index++)
        {
            var expected = 0.005 * Math.Pow(2, index);
            Check(SuperDraft.CurseRisk(index) == expected, "Curse risk doubles for offer " + (index + 1));
            var guaranteed = (int)Math.Floor(expected);
            Check(SuperDraft.CurseCount(expected, 0) == guaranteed + 1 &&
                SuperDraft.CurseCount(expected, 0.999) == guaranteed, "Whole and fractional Curse chances work at " + expected);
        }
        Check(SuperDraft.CurseCount(1.28, 0.279999) == 2 && SuperDraft.CurseCount(1.28, 0.28) == 1 &&
            SuperDraft.CurseCount(2.56, 0.55) == 3 && SuperDraft.CurseCount(2.56, 0.57) == 2,
            "128 percent and later offers grant guaranteed Curses plus a fractional roll");

        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run,
            new ModifierModel[] { ModelDb.Modifier<MustHave>().ToMutable(), ModelDb.Modifier<CardSwarm>().ToMutable() });
        var reward = (MegaCrit.Sts2.Core.Rewards.CardReward)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Rewards.CardReward));
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Rewards.Reward), "<Player>k__BackingField").SetValue(reward, player);
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Rewards.CardReward), "<CanSkip>k__BackingField").SetValue(reward, true);
        Check(!reward.CanSkip && MustHave.IsPending(reward), "Must Have forbids skipping an unclaimed card reward");
        var alternatives = new List<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative>
        {
            Alternative("Skip", MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward),
            Alternative("REROLL", MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction.DoNothing),
            Alternative("Heal", MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction.EndSelectionAndCompleteReward)
        };
        MustHave.FilterAlternatives(alternatives);
        Check(alternatives.Single().OptionId == "REROLL", "Must Have preserves rerolls and rejects card substitutes");
        var set = (MegaCrit.Sts2.Core.Rewards.RewardsSet)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet));
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Rewards.RewardsSet), "<Rewards>k__BackingField").SetValue(set,
            new List<MegaCrit.Sts2.Core.Rewards.Reward> { reward, new MegaCrit.Sts2.Core.Rewards.GoldReward(20, player) });
        Check(MustHave.BlocksLeaving(set), "Unclaimed card rewards block leaving a mixed reward set");
        AccessTools.Property(typeof(MegaCrit.Sts2.Core.Rewards.Reward), "SuccessfullySelected").SetValue(reward, true);
        Check(!MustHave.BlocksLeaving(set), "Non-card rewards remain optional after taking the required card");
        MustHave.AllowPassing(reward);
        Check(reward.CanSkip && !MustHave.RequiresCard(reward), "Super Draft passing is exempt from Must Have");
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        Check(reward.CanSkip && !MustHave.BlocksLeaving(set), "Runs without Must Have retain optional card rewards");

        static MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative Alternative(
            string id, MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction action)
        {
            // The native constructor initializes Godot input StringNames; keep this fixture engine-free.
            var alternative = (MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative)
                RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative));
            AccessTools.Field(alternative.GetType(), "<OptionId>k__BackingField").SetValue(alternative, id);
            AccessTools.Property(alternative.GetType(), "AfterSelected").SetValue(alternative, action);
            return alternative;
        }
    }

    private static void TestSealedSliders()
    {
        var canonical = ModelDb.Modifier<SealedDeck>();
        Check(ModifierValues.Get(canonical) == 10 && ModifierValues.GetSealedPool(canonical) == 30 &&
            ModifierValues.SealedPoolSpec == new ModifierValues.Spec(10, 60, 5, 30, "offers"),
            "Sealed Deck defaults to 10 of 30; pool slider uses 10..60, step 5");
        for (var pool = 10; pool <= 60; pool += 5)
        {
            var model = canonical.ToMutable();
            ModifierValues.SetSealedPool(model, pool);
            Check(ModifierValues.For(model)!.Max == pool - 5, "Sealed Deck pick maximum is pool minus five: " + pool);
            for (var selected = 5; selected <= pool - 5; selected += 5)
            {
                ModifierValues.Set(model, selected);
                var saved = model.ToSerializable();
                var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
                saved.Serialize(writer);
                var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
                reader.Reset(writer.Buffer);
                foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                    ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                    (ModifierModel)model.MutableClone() })
                    Check(ModifierValues.Get(restored) == selected && ModifierValues.GetSealedPool(restored) == pool,
                        $"Sealed Deck {selected} of {pool} survives save, network or clone");
            }
            ModifierValues.Set(model, int.MaxValue);
            Check(ModifierValues.Get(model) == pool - 5, "Pick count clamps to the current pool limit");
        }
        var shrinking = canonical.ToMutable();
        ModifierValues.SetSealedPool(shrinking, 60);
        ModifierValues.Set(shrinking, 55);
        ModifierValues.SetSealedPool(shrinking, 20);
        Check(ModifierValues.Get(shrinking) == 15, "Reducing the pool automatically clamps the selected card count");
        ModifierValues.SetSealedPool(shrinking, int.MinValue);
        Check(ModifierValues.GetSealedPool(shrinking) == 10 && ModifierValues.Get(shrinking) == 5,
            "Minimum pool leaves five selected cards");
        ModifierValues.SetSealedPool(shrinking, int.MaxValue);
        Check(ModifierValues.GetSealedPool(shrinking) == 60 && ModifierValues.Get(shrinking) == 5,
            "Increasing the pool preserves the current selected count and caps at sixty");
        ModifierValues.SetSealedPool(shrinking, 33);
        Check(ModifierValues.GetSealedPool(shrinking) == 35, "Pool values snap to step five");
        var oldSave = canonical.ToMutable();
        ModifierValues.Set(oldSave, 20);
        var savedOld = oldSave.ToSerializable();
        savedOld.Props!.ints!.RemoveAll(property => property.name == ModifierValues.SealedPoolKey);
        var oldRestored = ModifierModel.FromSerializable(savedOld);
        Check(ModifierValues.Get(oldRestored) == 20 && ModifierValues.GetSealedPool(oldRestored) == 30,
            "Older saves without a pool property retain the default pool of thirty");
        Check(ModifierValues.SealedDescriptionText("Choose [blue]10[/blue] from [blue]30[/blue].", 30, 60)
            == "Choose [blue]30[/blue] from [blue]60[/blue].", "Description substitutes both values without confusing equal numbers");
        var stateMachine = AccessTools.AsyncMoveNext(AccessTools.Method(typeof(SealedDeck), "ChooseCards"));
        var instructions = PatchProcessor.GetCurrentInstructions(stateMachine);
        Check(instructions.Count(instruction => instruction.operand is MethodInfo method &&
            method.DeclaringType == typeof(ModifierValues) && method.Name == nameof(ModifierValues.SealedPoolForPlayer)) == 1 &&
            instructions.Count(instruction => instruction.operand is MethodInfo method &&
            method.DeclaringType == typeof(ModifierValues) && method.Name == nameof(ModifierValues.FixedSealedPool)) == 1,
            "Native Sealed Deck resolves the configured pool once and prevents reward hooks changing its size");
        var preset = new ModifierPresetEntry { Id = canonical.Id.ToString(), Value = 55, SealedPoolSize = 60 };
        var restoredPreset = System.Text.Json.JsonSerializer.Deserialize<ModifierPresetEntry>(
            System.Text.Json.JsonSerializer.Serialize(preset))!;
        Check(restoredPreset.Value == 55 && restoredPreset.SealedPoolSize == 60,
            "Presets preserve both Sealed Deck sliders");
        Check(!ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers).Any(modifier => modifier.GetType().Name == "SuperSealed"),
            "Super Sealed is removed from the Custom Run menu");
        Check(ModifierValues.Get(canonical) == 10 && ModifierValues.GetSealedPool(canonical) == 30,
            "Custom Sealed Deck settings do not leak into canonical Daily defaults");
    }

    private static void TestSpeedrun()
    {
        var canonical = ModelDb.Modifier<Speedrun>();
        Check(ModifierValues.For(canonical) == new ModifierValues.Spec(10, 60, 5, 30, "minutes"),
            "Speedrun limit uses 10..60 minutes, step 5, default 30");
        Check(ModifierGroups.Classify(canonical, new HashSet<Type>()) == ModifierGroup.Negatives &&
            ModifierListPatch.IsCustomOnly(canonical) &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<Speedrun>().Count() == 1,
            "Speedrun appears once under Negatives and stays out of Daily pools");
        foreach (var limit in Enumerable.Range(0, 11).Select(index => 10 + index * 5))
        {
            Check(Speedrun.DueMinutes(limit * 60 - 1, limit) == 0 &&
                Speedrun.DueMinutes(limit * 60, limit) == 0 &&
                Speedrun.DueMinutes(limit * 60 + 59, limit) == 0 &&
                Speedrun.DueMinutes((limit + 1) * 60, limit) == 1 &&
                Speedrun.DueMinutes((limit + 4) * 60 + 59, limit) == 4,
                "Speedrun charges only full minutes after limit " + limit);
            var model = canonical.ToMutable();
            ModifierValues.Set(model, limit);
            ((Speedrun)model).SpeedrunPenaltyMinutes = 4;
            var saved = model.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                (ModifierModel)model.MutableClone() })
                Check(restored is Speedrun speedrun && speedrun.SpeedrunPenaltyMinutes == 4 &&
                    ModifierValues.Get(restored) == limit,
                    "Speedrun limit and paid penalties survive save, network and clone: " + limit);
        }
        var adjustable = canonical.ToMutable();
        ModifierValues.Set(adjustable, 33);
        Check(ModifierValues.Get(adjustable) == 35, "Speedrun limit snaps to five-minute steps");
        ModifierValues.Set(adjustable, int.MinValue);
        Check(ModifierValues.Get(adjustable) == 10, "Speedrun clamps its lower limit");
        ModifierValues.Set(adjustable, int.MaxValue);
        Check(ModifierValues.Get(adjustable) == 60 && ModifierValues.Get(canonical) == 30,
            "Speedrun clamps its upper limit without changing canonical defaults");
        var pulseWriter = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
        new NetSpeedrunPenaltyAction { Due = 7 }.Serialize(pulseWriter);
        var pulseReader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
        pulseReader.Reset(pulseWriter.Buffer);
        var pulse = new NetSpeedrunPenaltyAction();
        pulse.Deserialize(pulseReader);
        Check(pulse.Due == 7, "Host penalty count survives network serialization");
        var penalties = (Speedrun)canonical.ToMutable();
        var lostHp = 0;
        Task LoseHp() { lostHp += Speedrun.HpLoss; return Task.CompletedTask; }
        penalties.ApplyMinutes(3, LoseHp, () => false).GetAwaiter().GetResult();
        Check(lostHp == 15 && penalties.SpeedrunPenaltyMinutes == 3,
            "Delayed timer applies five HP for each overdue minute");
        var resumed = (Speedrun)ModifierModel.FromSerializable(penalties.ToSerializable());
        resumed.ApplyMinutes(3, LoseHp, () => false).GetAwaiter().GetResult();
        resumed.ApplyMinutes(2, LoseHp, () => false).GetAwaiter().GetResult();
        Check(lostHp == 15, "Reloads and repeated or stale pulses do not charge paid minutes again");
        resumed.ApplyMinutes(5, LoseHp, () => false).GetAwaiter().GetResult();
        Check(lostHp == 25 && resumed.SpeedrunPenaltyMinutes == 5,
            "Resumed timer charges only newly elapsed minutes");
        resumed.ApplyMinutes(8, LoseHp, () => lostHp >= 30).GetAwaiter().GetResult();
        Check(lostHp == 30 && resumed.SpeedrunPenaltyMinutes == 6,
            "Pending penalties stop as soon as the run ends");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.TopBar.NRunTimer), "OnTimerTimeout"))
            ?.Owners.Contains(ModEntry.HarmonyId) == true, "Native one-second run timer is patched");
    }

    private static void TestPresetManagement()
    {
        var empty = ModifierPresetStore.Empty;
        Check(empty.Name == "Empty" && empty.Modifiers.Count == 0,
            "Built-in Empty preset selects no modifiers");
        empty.Modifiers.Add(new ModifierPresetEntry { Id = "MODIFIER.SPEEDRUN", Value = 30 });
        Check(ModifierPresetStore.Empty.Modifiers.Count == 0,
            "Empty preset is recreated without inheriting modified entries");
        Check(ModifierPresetStore.IsReservedName(" eMpTy ") && !ModifierPresetStore.IsReservedName("Empty deck"),
            "Built-in Empty name is reserved without blocking other preset names");
        var keep = new ModifierPreset
        {
            Name = "Keep", Modifiers = [new ModifierPresetEntry { Id = "MODIFIER.SEALED_DECK", Value = 10, SealedPoolSize = 30 }]
        };
        var presets = new List<ModifierPreset>
        {
            new() { Name = "Remove" }, keep, new() { Name = "Remove another" }
        };
        Check(ModifierPresetStore.Remove(presets, "rEmOvE") && presets.Count == 2 && ReferenceEquals(presets[0], keep),
            "Deleting matches the complete name case-insensitively and preserves other presets");
        var restored = System.Text.Json.JsonSerializer.Deserialize<List<ModifierPreset>>(
            System.Text.Json.JsonSerializer.Serialize(presets))!;
        Check(restored.Select(preset => preset.Name).SequenceEqual(new[] { "Keep", "Remove another" }) &&
            restored[0].Modifiers.Single() is { Value: 10, SealedPoolSize: 30 },
            "Deleted preset stays absent from serialized storage while other slider values survive");
        Check(!ModifierPresetStore.Remove(presets, "missing") && presets.Count == 2,
            "Deleting a missing preset leaves the list intact");
        var legacy = new List<ModifierPreset> { new() { Name = "Empty" } };
        Check(ModifierPresetStore.Remove(legacy, "Empty") && ModifierPresetStore.Empty.Modifiers.Count == 0,
            "Removing a legacy saved Empty does not affect the built-in preset");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NDropdown), "OpenDropdown"))
            ?.Owners.Contains(ModEntry.HarmonyId) == true,
            "Preset remove-button focus is restored after native dropdown navigation setup");
    }

    private static void TestUltimateStarter()
    {
        var model = ModelDb.Modifier<UltimateStarter>();
        Check(ModifierGroups.Classify(model, new HashSet<Type>()) == ModifierGroup.ImprovedStart &&
            ModifierListPatch.IsCustomOnly(model) && ModifierValues.For(model) == null &&
            !model.ClearsPlayerDeck && model.GenerateNeowOption(null!) == null &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<UltimateStarter>().Count() == 1,
            "Ultimate Starter appears once in Improved Start with a fixed count and no extra Neow prompt");
        var savedModifier = model.ToMutable().ToSerializable();
        var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
        savedModifier.Serialize(writer);
        var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
        reader.Reset(writer.Buffer);
        Check(ModifierModel.FromSerializable(savedModifier) is UltimateStarter &&
            ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()) is UltimateStarter,
            "Ultimate Starter survives save and multiplayer modifier serialization");

        // Use real native cards and piles without constructing engine-dependent player visuals.
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        var players = new List<Player>();
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, players);
        var characters = new CharacterModel[]
        {
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Silent>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Regent>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Necrobinder>(),
            ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Defect>()
        };
        foreach (var character in characters)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
            AccessTools.Field(typeof(Player), "<Deck>k__BackingField").SetValue(player, new CardPile(PileType.Deck));
            players.Add(player);
            foreach (var canonical in character.StartingDeck)
            {
                var card = canonical.ToMutable();
                run.AddCard(card, player);
                card.FloorAddedToDeck = 1;
                player.Deck.AddInternal(card, silent: true);
            }
        }
        var original = players.Select(player => player.Deck.Cards.ToArray()).ToArray();
        UltimateStarter.ReplaceStarters(run);
        Check(players.Select((player, index) => player.Deck.Cards.SequenceEqual(original[index])).All(unchanged => unchanged),
            "Starting decks stay untouched without Ultimate Starter selected");
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run,
            new ModifierModel[] { model.ToMutable(), ModelDb.Modifier<Hoarder>().ToMutable() });
        UltimateStarter.ReplaceStarters(run);
        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];
            var special = original[index].Where(card => !card.IsBasicStrikeOrDefend).ToArray();
            var basics = original[index].Where(card => card.IsBasicStrikeOrDefend).ToArray();
            var deck = player.Deck.Cards;
            Check(deck.OfType<UltimateStrike>().Count() == 3 && deck.OfType<UltimateDefend>().Count() == 3 &&
                deck.Count == special.Length + 6 && special.All(deck.Contains),
                characters[index].GetType().Name + " receives three of each Ultimate card and preserves special starters");
            Check(basics.All(card => card.HasBeenRemovedFromState && !run.ContainsCard(card)) &&
                deck.All(card => card.Owner == player && run.ContainsCard(card)) &&
                deck.Where(card => card is UltimateStrike or UltimateDefend).All(card => card.FloorAddedToDeck == 1 && !card.IsUpgraded),
                "Replaced cards leave the run; Ultimate cards have correct ownership and starting metadata");
            var restored = deck.Select(card => CardModel.FromSerializable(card.ToSerializable())).ToArray();
            Check(restored.OfType<UltimateStrike>().Count() == 3 && restored.OfType<UltimateDefend>().Count() == 3 &&
                restored.Select(card => card.Id).SequenceEqual(deck.Select(card => card.Id)),
                "Resulting starter deck round-trips through native card save serialization");
        }
        var completed = players.Select(player => player.Deck.Cards.ToArray()).ToArray();
        UltimateStarter.ReplaceStarters(run);
        model.ToMutable().OnRunLoaded(run);
        Check(players.Select((player, index) => player.Deck.Cards.SequenceEqual(completed[index])).All(unchanged => unchanged),
            "Repeated startup replacement and modifier reload never duplicate Ultimate starters");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(RunState), nameof(RunState.CreateForNewRun)))
            ?.Owners.Contains(ModEntry.HarmonyId) == true &&
            Harmony.GetPatchInfo(AccessTools.Method(typeof(RunState), nameof(RunState.FromSerializable)))
                ?.Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(UltimateStarterNewRunPatch)) != true,
            "Ultimate Starter patches new-run creation only");
    }

    private static void TestDill()
    {
        var canonical = ModelDb.Modifier<Dill>();
        Check(ModifierValues.For(canonical) == new ModifierValues.Spec(1, 20, 1, 1, "initial max HP") &&
            Dill.GrowthSpec == new ModifierValues.Spec(1, 5, 1, 2, "max HP per fight") &&
            canonical.MaxHpPerFight == 2,
            "Dill has independent initial-HP and per-fight sliders with the requested ranges and defaults");
        Check(ModifierGroups.Classify(canonical, new HashSet<Type>()) == ModifierGroup.Negatives &&
            ModifierListPatch.IsCustomOnly(canonical) &&
            ModifierListPatch.ForCustomRun(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers)).OfType<Dill>().Count() == 1,
            "Dill appears once under Negatives without entering Daily pools");
        for (var initial = 1; initial <= 20; initial++)
        for (var growth = 1; growth <= 5; growth++)
        {
            var modifier = (Dill)canonical.ToMutable();
            ModifierValues.Set(modifier, initial);
            modifier.MaxHpPerFight = growth;
            var saved = modifier.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            foreach (var restored in new[] { ModifierModel.FromSerializable(saved),
                ModifierModel.FromSerializable(reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>()),
                (ModifierModel)modifier.MutableClone() })
                Check(restored is Dill dill && ModifierValues.Get(dill) == initial && dill.MaxHpPerFight == growth,
                    $"Dill {initial} initial HP and {growth} growth survive save, network and clone");
            Check(ModifierValues.DillDescriptionText(Dill.DisplayDescription, initial, growth) ==
                $"Start the game with [blue]{initial}[/blue] max HP. Each fight increases max HP by [blue]{growth}[/blue].",
                "Dill description updates both independent values without substitution collisions");
        }
        var configurable = (Dill)canonical.ToMutable();
        ModifierValues.Set(configurable, int.MinValue);
        configurable.MaxHpPerFight = int.MinValue;
        Check(ModifierValues.Get(configurable) == 1 && configurable.MaxHpPerFight == 1,
            "Dill sliders clamp at their lower limits");
        ModifierValues.Set(configurable, int.MaxValue);
        configurable.MaxHpPerFight = int.MaxValue;
        Check(ModifierValues.Get(configurable) == 20 && configurable.MaxHpPerFight == 5 &&
            ModifierValues.Get(canonical) == 1 && canonical.MaxHpPerFight == 2,
            "Dill sliders clamp at their upper limits without changing canonical defaults");
        var missingGrowth = configurable.ToSerializable();
        missingGrowth.Props!.ints!.RemoveAll(property => property.name == nameof(Dill.MaxHpPerFight));
        Check(ModifierModel.FromSerializable(missingGrowth) is Dill { MaxHpPerFight: 2 },
            "Dill saves without a growth property retain its default of two");
        var preset = new ModifierPresetEntry { Id = canonical.Id.ToString(), Value = 17, MaxHpPerFight = 4 };
        var restoredPreset = System.Text.Json.JsonSerializer.Deserialize<ModifierPresetEntry>(
            System.Text.Json.JsonSerializer.Serialize(preset))!;
        Check(restoredPreset is { Value: 17, MaxHpPerFight: 4 }, "Presets preserve both Dill sliders");

        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        var players = new List<Player>();
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, players);
        var setMax = AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "MaxHp");
        var setCurrent = AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "CurrentHp");
        for (var index = 0; index < 3; index++)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            var creature = (MegaCrit.Sts2.Core.Entities.Creatures.Creature)RuntimeHelpers.GetUninitializedObject(
                typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature));
            AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(player, creature);
            AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
            setMax.Invoke(creature, [80]);
            setCurrent.Invoke(creature, [60]);
            players.Add(player);
        }
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, Array.Empty<ModifierModel>());
        Dill.InitializeHealth(run);
        Check(players.All(player => player.Creature.MaxHp == 80 && player.Creature.CurrentHp == 60),
            "Runs without Dill preserve normal starting HP");
        foreach (var initial in new[] { 1, 7, 20 })
        {
            ModifierValues.Set(configurable, initial);
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { configurable });
            Dill.InitializeHealth(run);
            Check(players.All(player => player.Creature.MaxHp == initial && player.Creature.CurrentHp == initial),
                "Dill sets every co-op player's current and max HP to " + initial);
        }
        configurable.OnRunCreated(run);
        configurable.MaxHpPerFight = 4;
        setCurrent.Invoke(players[2].Creature, [0]);
        var gains = new List<(MegaCrit.Sts2.Core.Entities.Creatures.Creature Creature, decimal Amount)>();
        configurable.GrowAfterVictory((creature, amount) =>
        {
            gains.Add((creature, amount));
            setMax.Invoke(creature, [creature.MaxHp + (int)amount]);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
        Check(gains.Count == 2 && gains.All(gain => gain.Amount == 4) &&
            players.Take(2).All(player => player.Creature.MaxHp == 24) && players[2].Creature.MaxHp == 20,
            "Each victory grants the configured max HP once to surviving players without reviving dead teammates");
        configurable.OnRunLoaded(run);
        Check(players.Take(2).All(player => player.Creature.MaxHp == 24),
            "Loading Dill does not reset max HP earned from combat");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(RunManager), "InitializeNewRun"))
            ?.Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(DillStartingHealthPatch)) == true &&
            Harmony.GetPatchInfo(AccessTools.Method(typeof(RunManager), "InitializeSavedRun"))
                ?.Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(DillStartingHealthPatch)) != true &&
            typeof(Dill).GetMethod(nameof(AbstractModel.AfterCombatVictory))!.DeclaringType == typeof(Dill) &&
            typeof(Dill).GetMethod(nameof(AbstractModel.BeforeCombatStart))!.DeclaringType != typeof(Dill),
            "Dill starts at exact HP only on new runs and grows after victories rather than before fights");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}

internal class TestNeow : Neow
{
    public bool FailGeneration;
    public IReadOnlyList<EventOption>? OptionsShown;
    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        if (FailGeneration) throw new InvalidOperationException("Test generation failure");
        if (NeowRelicChoice.ModifierCount(2, this) != 0) throw new Exception("Vanilla path was not enabled");
        return Enumerable.Range(0, 3).Select(_ =>
            (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption))).ToArray();
    }
    protected override void SetEventState(LocString description, IEnumerable<EventOption> options) =>
        OptionsShown = options.ToArray();
}
